using ChartHub.Server.Contracts;
using ChartHub.Server.Services;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace ChartHub.Server.Endpoints;

/// <summary>
/// JWT-authenticated endpoints for administering runners (issuing tokens, listing, deregistering).
/// These endpoints are only accessible by a logged-in ChartHub user.
/// </summary>
public static class RunnerManagementEndpoints
{
    public static RouteGroupBuilder MapRunnerManagementEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app
            .MapGroup("/api/v1/runners")
            .RequireAuthorization()
            .WithTags("Runners");

        // POST /api/v1/runners/registration-tokens
        group.MapPost("/registration-tokens", (
            [FromBody] IssueRunnerRegistrationTokenRequest request,
            ITranscriptionRunnerRegistry registry) =>
        {
            int ttlMinutes = Math.Clamp(request.TtlMinutes, 1, 60);
            RunnerRegistrationTokenResponse token = registry.IssueRegistrationToken(TimeSpan.FromMinutes(ttlMinutes));
            return Results.Ok(token);
        })
        .WithName("IssueRunnerRegistrationToken")
        .WithSummary("Issue a one-time runner registration token.");

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
