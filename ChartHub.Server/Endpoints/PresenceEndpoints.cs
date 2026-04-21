using System.Net.WebSockets;
using System.Security.Claims;

using ChartHub.Server.Services;

namespace ChartHub.Server.Endpoints;

public static partial class PresenceEndpoints
{
    public static IEndpointRouteBuilder MapPresenceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/presence/ws", HandlePresenceAsync)
            .WithName("PresenceWebSocket")
            .WithTags("Presence")
            .WithSummary("WebSocket presence endpoint. Android opens on login; close = user gone.")
            .Produces(StatusCodes.Status101SwitchingProtocols)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        return app;
    }

    private static async Task HandlePresenceAsync(
        HttpContext context,
        IPresenceTracker presenceTracker,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        string userEmail = context.User.FindFirstValue(ClaimTypes.Email)
            ?? context.User.FindFirstValue("email")
            ?? string.Empty;

        string deviceName = DeviceNameNormalizer.Normalize(context.Request.Headers["X-Device-Name"].FirstOrDefault());

        using WebSocket ws = await context.WebSockets.AcceptWebSocketAsync();

        if (!presenceTracker.Register(deviceName, userEmail))
        {
            await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "presence slot occupied", CancellationToken.None);
            LogPresenceRejected(logger, deviceName, userEmail);
            return;
        }

        LogPresenceConnected(logger, deviceName, userEmail);

        try
        {
            // Hold the WebSocket open; drain incoming pings/close frames.
            byte[] buffer = new byte[64];

            while (ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                WebSocketReceiveResult result;

                try
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (WebSocketException)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "client closed", CancellationToken.None);
                    break;
                }
            }
        }
        finally
        {
            presenceTracker.Unregister(deviceName);
        }

        LogPresenceDisconnected(logger, deviceName, userEmail);
    }

    [LoggerMessage(EventId = 8200, Level = LogLevel.Information,
        Message = "Presence connected. Device={DeviceName}, User={UserEmail}")]
    private static partial void LogPresenceConnected(ILogger logger, string deviceName, string userEmail);

    [LoggerMessage(EventId = 8201, Level = LogLevel.Information,
        Message = "Presence disconnected. Device={DeviceName}, User={UserEmail}")]
    private static partial void LogPresenceDisconnected(ILogger logger, string deviceName, string userEmail);

    [LoggerMessage(EventId = 8202, Level = LogLevel.Warning,
        Message = "Presence rejected (slot occupied). Device={DeviceName}, User={UserEmail}")]
    private static partial void LogPresenceRejected(ILogger logger, string deviceName, string userEmail);
}
