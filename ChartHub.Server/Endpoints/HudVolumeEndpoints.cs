using System.Net;
using System.Text.Json;

using ChartHub.Server.Contracts;
using ChartHub.Server.Services;

namespace ChartHub.Server.Endpoints;

public static partial class HudVolumeEndpoints
{
    private static readonly JsonSerializerOptions SseJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapHudVolumeEndpoints(this IEndpointRouteBuilder app)
    {
        // No authentication — restricted to loopback callers only.
        app.MapGet("/api/v1/hud/volume/stream", StreamHudVolumeAsync)
            .WithName("StreamHudVolume")
            .WithTags("Hud")
            .WithSummary("SSE stream of HUD volume updates (loopback only, no auth required).")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> StreamHudVolumeAsync(
        HttpContext context,
        IVolumeService volumeService,
        CancellationToken cancellationToken)
    {
        if (!IsLoopbackCaller(context))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        context.Response.Headers.Append("Content-Type", "text/event-stream");
        context.Response.Headers.Append("Cache-Control", "no-cache");

        long observedChangeStamp = volumeService.CurrentChangeStamp;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                HudVolumePayload payload;

                try
                {
                    VolumeStateResponse state = await volumeService.GetStateAsync(cancellationToken).ConfigureAwait(false);
                    payload = new HudVolumePayload
                    {
                        IsAvailable = state.SupportsMasterVolume,
                        ValuePercent = state.Master.ValuePercent,
                        IsMuted = state.Master.IsMuted,
                    };
                }
                catch (VolumeServiceException ex) when (ex.StatusCode == StatusCodes.Status501NotImplemented)
                {
                    payload = new HudVolumePayload
                    {
                        IsAvailable = false,
                        ValuePercent = 0,
                        IsMuted = false,
                    };
                }

                await context.Response.WriteAsync("event: hud-volume\n", cancellationToken).ConfigureAwait(false);
                await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(payload, SseJsonOptions)}\n\n", cancellationToken).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);

                observedChangeStamp = volumeService.CurrentChangeStamp;
                await volumeService.WaitForChangeAsync(
                        observedChangeStamp,
                        TimeSpan.FromSeconds(Math.Max(1, volumeService.SseHeartbeatSeconds)),
                        cancellationToken)
                    .ConfigureAwait(false);
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

    private sealed class HudVolumePayload
    {
        public bool IsAvailable { get; init; }
        public int ValuePercent { get; init; }
        public bool IsMuted { get; init; }
    }
}