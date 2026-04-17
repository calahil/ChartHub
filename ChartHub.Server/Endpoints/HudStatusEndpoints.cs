using System.Net;
using System.Text.Json;

using ChartHub.Server.Services;

namespace ChartHub.Server.Endpoints;

public static partial class HudStatusEndpoints
{
    public static IEndpointRouteBuilder MapHudStatusEndpoints(this IEndpointRouteBuilder app)
    {
        // No authentication — restricted to loopback callers only (see LoopbackOnly guard below).
        // The HUD subprocess connects from localhost; exposing this unauthenticated on the network
        // would allow anyone to observe the connection count.
        app.MapGet("/api/v1/hud/status/stream", StreamHudStatusAsync)
            .WithName("StreamHudStatus")
            .WithTags("Hud")
            .WithSummary("SSE stream of HUD status updates (loopback only, no auth required).")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> StreamHudStatusAsync(
        HttpContext context,
        IInputConnectionTracker tracker,
        CancellationToken cancellationToken)
    {
        if (!IsLoopbackCaller(context))
        {
            return Results.Forbid();
        }

        context.Response.Headers.Append("Content-Type", "text/event-stream");
        context.Response.Headers.Append("Cache-Control", "no-cache");

        try
        {
            await foreach (int count in tracker.WatchAsync(cancellationToken).ConfigureAwait(false))
            {
                HudStatusPayload payload = new() { ConnectedDeviceCount = count };
                await context.Response.WriteAsync("event: hud-status\n", cancellationToken).ConfigureAwait(false);
                await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(payload)}\n\n", cancellationToken).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            return Results.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Results.Empty;
        }
    }

    private static bool IsLoopbackCaller(HttpContext context)
    {
        IPAddress? remote = context.Connection.RemoteIpAddress;
        return remote is not null && IPAddress.IsLoopback(remote);
    }

    private sealed class HudStatusPayload
    {
        public int ConnectedDeviceCount { get; init; }
    }
}
