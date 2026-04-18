using ChartHub.Server.Services;

namespace ChartHub.Server.Middleware;

/// <summary>
/// Validates <c>Authorization: Runner {runnerId}:{secret}</c> headers on runner protocol
/// endpoints and populates <see cref="HttpContext.Items"/> with the authenticated runner ID.
/// </summary>
public sealed class RunnerAuthMiddleware
{
    public const string RunnerIdKey = "RunnerAuth:RunnerId";
    public const string RunnerRecordKey = "RunnerAuth:RunnerRecord";

    private readonly RequestDelegate _next;

    public RunnerAuthMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ITranscriptionRunnerRegistry registry)
    {
        if (!TryParseRunnerHeader(context, out string? runnerId, out string? secret))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Missing or malformed Authorization: Runner header." });
            return;
        }

        TranscriptionRunnerRecord? runner = registry.ValidateRunner(runnerId!, secret!);
        if (runner is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid runner credentials." });
            return;
        }

        context.Items[RunnerIdKey] = runner.RunnerId;
        context.Items[RunnerRecordKey] = runner;

        await _next(context);
    }

    private static bool TryParseRunnerHeader(
        HttpContext context,
        out string? runnerId,
        out string? secret)
    {
        runnerId = null;
        secret = null;

        string? header = context.Request.Headers.Authorization;
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Runner ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ReadOnlySpan<char> payload = header.AsSpan("Runner ".Length);
        int colon = payload.IndexOf(':');
        if (colon <= 0 || colon == payload.Length - 1)
        {
            return false;
        }

        runnerId = payload[..colon].ToString();
        secret = payload[(colon + 1)..].ToString();
        return true;
    }
}

public static class RunnerAuthMiddlewareExtensions
{
    /// <summary>
    /// Applies <see cref="RunnerAuthMiddleware"/> and returns the route group for chaining.
    /// </summary>
    public static RouteGroupBuilder RequireRunnerAuth(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter(async (ctx, next) =>
        {
            if (ctx.HttpContext.Items[RunnerAuthMiddleware.RunnerIdKey] is not string)
            {
                return Results.Json(new { error = "Runner authentication required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            return await next(ctx);
        });

        return group;
    }

    /// <summary>
    /// Returns the authenticated runner ID from <see cref="HttpContext.Items"/>.
    /// Throws if the request was not authenticated by <see cref="RunnerAuthMiddleware"/>.
    /// </summary>
    public static string GetRunnerId(this HttpContext ctx)
    {
        if (ctx.Items[RunnerAuthMiddleware.RunnerIdKey] is string id)
        {
            return id;
        }

        throw new InvalidOperationException("Runner ID not found in HttpContext. Ensure RunnerAuthMiddleware has been applied.");
    }

    /// <summary>
    /// Returns the full <see cref="TranscriptionRunnerRecord"/> from <see cref="HttpContext.Items"/>.
    /// </summary>
    public static TranscriptionRunnerRecord GetRunnerRecord(this HttpContext ctx)
    {
        if (ctx.Items[RunnerAuthMiddleware.RunnerRecordKey] is TranscriptionRunnerRecord record)
        {
            return record;
        }

        throw new InvalidOperationException("Runner record not found in HttpContext. Ensure RunnerAuthMiddleware has been applied.");
    }
}
