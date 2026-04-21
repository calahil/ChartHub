using System.Net;
using System.Text.Json;

using ChartHub.Server.Services;

namespace ChartHub.Server.Endpoints;

public static partial class HudStatusEndpoints
{
    private static readonly JsonSerializerOptions SseJsonOptions = new(JsonSerializerDefaults.Web);

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
        IPresenceTracker presenceTracker,
        IUinputGamepadService gamepad,
        IUinputMouseService mouse,
        IUinputKeyboardService keyboard,
        CancellationToken cancellationToken)
    {
        if (!IsLoopbackCaller(context))
        {
            return Results.Forbid();
        }

        context.Response.Headers.Append("Content-Type", "text/event-stream");
        context.Response.Headers.Append("Cache-Control", "no-cache");

        bool uinputAvailable = gamepad.IsSupported && mouse.IsSupported && keyboard.IsSupported;

        try
        {
            await foreach (PresenceUpdate update in presenceTracker.WatchAsync(cancellationToken).ConfigureAwait(false))
            {
                HudStatusPayload payload = new()
                {
                    IsPresent = update.IsPresent,
                    DeviceName = update.DeviceName,
                    UserEmail = update.UserEmail,
                    UinputAvailable = uinputAvailable,
                };
                await context.Response.WriteAsync("event: hud-status\n", cancellationToken).ConfigureAwait(false);
                await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(payload, SseJsonOptions)}\n\n", cancellationToken).ConfigureAwait(false);
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
        public bool IsPresent { get; init; }
        public string? DeviceName { get; init; }
        public string? UserEmail { get; init; }
        public bool UinputAvailable { get; init; }
    }
}

