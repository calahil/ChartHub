using System.Net.WebSockets;
using System.Text.Json;

using ChartHub.Server.Contracts;
using ChartHub.Server.Services;

using Microsoft.Extensions.Logging;

namespace ChartHub.Server.Endpoints;

public static partial class InputEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapInputEndpoints(this IEndpointRouteBuilder app)
    {
        // WebSocket routes cannot use standard Produces() metadata; they upgrade the
        // connection before a response body is written. Auth is enforced on the HTTP
        // upgrade handshake via RequireAuthorization().
        IEndpointRouteBuilder group = app.MapGroup("/api/v1/input")
            .WithTags("Input")
            .RequireAuthorization();

        group.MapGet("/controller/ws", HandleControllerAsync)
            .WithName("InputControllerWebSocket")
            .WithSummary("WebSocket endpoint for virtual gamepad input (buttons + D-pad).")
            .Produces(StatusCodes.Status101SwitchingProtocols)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/touchpad/ws", HandleTouchpadAsync)
            .WithName("InputTouchpadWebSocket")
            .WithSummary("WebSocket endpoint for virtual mouse input (pointer movement + buttons).")
            .Produces(StatusCodes.Status101SwitchingProtocols)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/keyboard/ws", HandleKeyboardAsync)
            .WithName("InputKeyboardWebSocket")
            .WithSummary("WebSocket endpoint for virtual keyboard input (key codes + characters).")
            .Produces(StatusCodes.Status101SwitchingProtocols)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task HandleControllerAsync(
        HttpContext context,
        IUinputGamepadService gamepad,
        IInputConnectionTracker connectionTracker,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        string deviceName = context.Request.Headers["X-Device-Name"].FirstOrDefault() ?? "unknown";

        using WebSocket ws = await context.WebSockets.AcceptWebSocketAsync();

        if (!connectionTracker.RegisterConnection(deviceName))
        {
            await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "connection slot occupied", CancellationToken.None);
            LogControllerRejected(logger, deviceName);
            return;
        }

        LogControllerConnected(logger, deviceName);

        try
        {
            await ReceiveLoopAsync(ws, logger, cancellationToken, message =>
            {
                string type = message.GetProperty("type").GetString() ?? string.Empty;

                if (string.Equals(type, "btn", StringComparison.OrdinalIgnoreCase))
                {
                    ControllerButtonMessage? msg = message.Deserialize<ControllerButtonMessage>(JsonOptions);
                    if (msg is not null)
                    {
                        gamepad.PressButton(msg.ButtonId, msg.Pressed);
                    }
                }
                else if (string.Equals(type, "dpad", StringComparison.OrdinalIgnoreCase))
                {
                    ControllerDPadMessage? msg = message.Deserialize<ControllerDPadMessage>(JsonOptions);
                    if (msg is not null)
                    {
                        gamepad.SetDPad(msg.X, msg.Y);
                    }
                }
            });
        }
        finally
        {
            connectionTracker.UnregisterConnection(deviceName);
        }

        LogControllerDisconnected(logger, deviceName);
    }

    private static async Task HandleTouchpadAsync(
        HttpContext context,
        IUinputMouseService mouse,
        IInputConnectionTracker connectionTracker,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        string deviceName = context.Request.Headers["X-Device-Name"].FirstOrDefault() ?? "unknown";

        using WebSocket ws = await context.WebSockets.AcceptWebSocketAsync();

        if (!connectionTracker.RegisterConnection(deviceName))
        {
            await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "connection slot occupied", CancellationToken.None);
            LogTouchpadRejected(logger, deviceName);
            return;
        }

        LogTouchpadConnected(logger, deviceName);

        try
        {
            await ReceiveLoopAsync(ws, logger, cancellationToken, message =>
            {
                string type = message.GetProperty("type").GetString() ?? string.Empty;

                if (string.Equals(type, "move", StringComparison.OrdinalIgnoreCase))
                {
                    TouchpadMoveMessage? msg = message.Deserialize<TouchpadMoveMessage>(JsonOptions);
                    if (msg is not null)
                    {
                        mouse.MoveDelta(msg.Dx, msg.Dy);
                    }
                }
                else if (string.Equals(type, "mousebtn", StringComparison.OrdinalIgnoreCase))
                {
                    TouchpadButtonMessage? msg = message.Deserialize<TouchpadButtonMessage>(JsonOptions);
                    if (msg is not null)
                    {
                        mouse.PressButton(msg.Side, msg.Pressed);
                    }
                }
            });
        }
        finally
        {
            connectionTracker.UnregisterConnection(deviceName);
        }

        LogTouchpadDisconnected(logger, deviceName);
    }

    private static async Task HandleKeyboardAsync(
        HttpContext context,
        IUinputKeyboardService keyboard,
        IInputConnectionTracker connectionTracker,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        string deviceName = context.Request.Headers["X-Device-Name"].FirstOrDefault() ?? "unknown";

        using WebSocket ws = await context.WebSockets.AcceptWebSocketAsync();

        if (!connectionTracker.RegisterConnection(deviceName))
        {
            await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "connection slot occupied", CancellationToken.None);
            LogKeyboardRejected(logger, deviceName);
            return;
        }

        LogKeyboardConnected(logger, deviceName);

        try
        {
            await ReceiveLoopAsync(ws, logger, cancellationToken, message =>
            {
                string type = message.GetProperty("type").GetString() ?? string.Empty;

                if (string.Equals(type, "key", StringComparison.OrdinalIgnoreCase))
                {
                    KeyboardKeyMessage? msg = message.Deserialize<KeyboardKeyMessage>(JsonOptions);
                    if (msg is not null)
                    {
                        keyboard.PressKey(msg.LinuxKeyCode, msg.Pressed);
                    }
                }
                else if (string.Equals(type, "char", StringComparison.OrdinalIgnoreCase))
                {
                    KeyboardCharMessage? msg = message.Deserialize<KeyboardCharMessage>(JsonOptions);
                    if (msg?.Char is { Length: > 0 } charStr)
                    {
                        keyboard.TypeChar(charStr[0]);
                    }
                }
            });
        }
        finally
        {
            connectionTracker.UnregisterConnection(deviceName);
        }

        LogKeyboardDisconnected(logger, deviceName);
    }

    private static async Task ReceiveLoopAsync(
        WebSocket ws,
        ILogger logger,
        CancellationToken cancellationToken,
        Action<JsonElement> dispatch)
    {
        byte[] buffer = new byte[4096];

        while (ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            using MemoryStream ms = new();

            do
            {
                try
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (WebSocketException ex)
                {
                    LogWebSocketError(logger, ex.Message);
                    return;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "client closed", CancellationToken.None);
                    return;
                }

                ms.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            ms.Seek(0, SeekOrigin.Begin);

            try
            {
                JsonDocument doc = await JsonDocument.ParseAsync(ms, cancellationToken: cancellationToken);
                dispatch(doc.RootElement);
            }
            catch (JsonException ex)
            {
                LogJsonParseError(logger, ex.Message);
            }
        }
    }

    [LoggerMessage(EventId = 8101, Level = LogLevel.Information, Message = "InputEndpoints: controller WebSocket connected. Device={DeviceName}")]
    private static partial void LogControllerConnected(ILogger logger, string deviceName);

    [LoggerMessage(EventId = 8102, Level = LogLevel.Information, Message = "InputEndpoints: controller WebSocket closed. Device={DeviceName}")]
    private static partial void LogControllerDisconnected(ILogger logger, string deviceName);

    [LoggerMessage(EventId = 8109, Level = LogLevel.Warning, Message = "InputEndpoints: controller WebSocket rejected — slot occupied. Device={DeviceName}")]
    private static partial void LogControllerRejected(ILogger logger, string deviceName);

    [LoggerMessage(EventId = 8103, Level = LogLevel.Information, Message = "InputEndpoints: touchpad WebSocket connected. Device={DeviceName}")]
    private static partial void LogTouchpadConnected(ILogger logger, string deviceName);

    [LoggerMessage(EventId = 8104, Level = LogLevel.Information, Message = "InputEndpoints: touchpad WebSocket closed. Device={DeviceName}")]
    private static partial void LogTouchpadDisconnected(ILogger logger, string deviceName);

    [LoggerMessage(EventId = 8110, Level = LogLevel.Warning, Message = "InputEndpoints: touchpad WebSocket rejected — slot occupied. Device={DeviceName}")]
    private static partial void LogTouchpadRejected(ILogger logger, string deviceName);

    [LoggerMessage(EventId = 8105, Level = LogLevel.Information, Message = "InputEndpoints: keyboard WebSocket connected. Device={DeviceName}")]
    private static partial void LogKeyboardConnected(ILogger logger, string deviceName);

    [LoggerMessage(EventId = 8106, Level = LogLevel.Information, Message = "InputEndpoints: keyboard WebSocket closed. Device={DeviceName}")]
    private static partial void LogKeyboardDisconnected(ILogger logger, string deviceName);

    [LoggerMessage(EventId = 8111, Level = LogLevel.Warning, Message = "InputEndpoints: keyboard WebSocket rejected — slot occupied. Device={DeviceName}")]
    private static partial void LogKeyboardRejected(ILogger logger, string deviceName);

    [LoggerMessage(EventId = 8107, Level = LogLevel.Debug, Message = "InputEndpoints: WebSocket receive error: {Message}")]
    private static partial void LogWebSocketError(ILogger logger, string message);

    [LoggerMessage(EventId = 8108, Level = LogLevel.Debug, Message = "InputEndpoints: malformed JSON message: {Message}")]
    private static partial void LogJsonParseError(ILogger logger, string message);
}

