using ChartHub.Server.Contracts;
using ChartHub.Server.Options;
using ChartHub.Server.Services;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ChartHub.Server.Endpoints;

/// <summary>
/// JWT-authenticated endpoints for administering runners (issuing tokens, listing, deregistering).
/// Most endpoints require a logged-in ChartHub user JWT.
/// The registration-token endpoint additionally accepts a static <c>X-Runner-Api-Key</c> header
/// so CI can issue tokens without a user login session.
/// </summary>
public static class RunnerManagementEndpoints
{
    internal const string ApiKeyHeader = "X-Runner-Api-Key";

    public static RouteGroupBuilder MapRunnerManagementEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app
            .MapGroup("/api/v1/runners")
            .RequireAuthorization()
            .WithTags("Runners");

        // POST /api/v1/runners/registration-tokens
        // Accepts either:
        //   - Authorization: Bearer <user-jwt>   (interactive user session)
        //   - X-Runner-Api-Key: <static-key>     (CI / automation — never expires)
        group.MapPost("/registration-tokens", (
            HttpContext ctx,
            [FromBody] IssueRunnerRegistrationTokenRequest request,
            ITranscriptionRunnerRegistry registry,
            IOptions<RunnerOptions> runnerOptions) =>
        {
            int ttlMinutes = Math.Clamp(request.TtlMinutes, 1, 60);
            RunnerRegistrationTokenResponse token = registry.IssueRegistrationToken(TimeSpan.FromMinutes(ttlMinutes));
            return Results.Ok(token);
        })
        .WithName("IssueRunnerRegistrationToken")
        .WithSummary("Issue a one-time runner registration token. Accepts user JWT or X-Runner-Api-Key header.")
        .AllowAnonymous()
        .AddEndpointFilter(async (ctx, next) =>
        {
            // Allow through if the request already carries a valid user JWT.
            if (ctx.HttpContext.User.Identity?.IsAuthenticated == true)
            {
                return await next(ctx);
            }

            // Fall back to static API key.
            IOptions<RunnerOptions> runnerOptions = ctx.HttpContext.RequestServices.GetRequiredService<IOptions<RunnerOptions>>();
            string configuredKey = runnerOptions.Value.ManagementApiKey;

            if (string.IsNullOrWhiteSpace(configuredKey))
            {
                return Results.Json(new { error = "Runner management API key is not configured on this server." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            if (!ctx.HttpContext.Request.Headers.TryGetValue(ApiKeyHeader, out Microsoft.Extensions.Primitives.StringValues providedKey)
                || !string.Equals(providedKey, configuredKey, StringComparison.Ordinal))
            {
                return Results.Json(new { error = $"Provide a valid user JWT or {ApiKeyHeader} header." },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            return await next(ctx);
        });

        // GET /api/v1/runners
        group.MapGet("/", (ITranscriptionRunnerRegistry registry) =>
        {
            IReadOnlyList<TranscriptionRunnerRecord> runners = registry.ListRunners();
            DateTimeOffset onlineThreshold = DateTimeOffset.UtcNow.AddMinutes(-2);
            var result = runners
                .Select(r => new RunnerSummaryResponse
                {
                    RunnerId = r.RunnerId,
                    RunnerName = r.RunnerName,
                    MaxConcurrency = r.MaxConcurrency,
                    RegisteredAtUtc = r.RegisteredAtUtc,
                    LastHeartbeatUtc = r.LastHeartbeatUtc,
                    LastActiveJobCount = r.LastActiveJobCount,
                    IsActive = r.IsActive,
                    IsOnline = r.IsActive && r.LastHeartbeatUtc.HasValue && r.LastHeartbeatUtc >= onlineThreshold,
                })
                .ToList();
            return Results.Ok(result);
        })
        .WithName("ListRunners")
        .WithSummary("List all registered transcription runners.");

        // DELETE /api/v1/runners/{runnerId}
        group.MapDelete("/{runnerId}", (
            string runnerId,
            ITranscriptionRunnerRegistry registry) =>
        {
            bool deregistered = registry.TryDeregisterRunner(runnerId);
            return deregistered
                ? Results.NoContent()
                : Results.NotFound(new { error = "Runner not found or already inactive." });
        })
        .WithName("DeregisterRunner")
        .WithSummary("Deactivate a transcription runner.");

        return group;
    }
}
