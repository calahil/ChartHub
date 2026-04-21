using System.Net.WebSockets;

using ChartHub.Utilities;

namespace ChartHub.Services;

public sealed class PresenceWebSocketService : IPresenceWebSocketService
{
    private ClientWebSocket? _ws;
    private bool _disposed;

    public async Task ConnectAsync(string baseUrl, string bearerToken, string deviceName, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Tear down any existing connection before reconnecting.
        await DisconnectAsync().ConfigureAwait(false);

        _ws = new ClientWebSocket();

        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            _ws.Options.SetRequestHeader("Authorization", $"Bearer {bearerToken}");
        }

        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            _ws.Options.SetRequestHeader("X-Device-Name", deviceName);
        }

        Uri uri = BuildWebSocketUri(baseUrl, "api/v1/presence/ws");

        try
        {
            await _ws.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError("Presence", "Presence WebSocket connect failed", ex);
            _ws.Dispose();
            _ws = null;
        }
    }

    public async Task DisconnectAsync()
    {
        if (_ws is null)
        {
            return;
        }

        if (_ws.State == WebSocketState.Open)
        {
            try
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "app backgrounded", CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
                // Connection may have already been terminated.
            }
        }

        _ws.Dispose();
        _ws = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ws?.Dispose();
        _ws = null;
    }

    private static Uri BuildWebSocketUri(string baseUrl, string path)
    {
        string trimmed = baseUrl.TrimEnd('/');
        string wsBase = trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? "wss://" + trimmed["https://".Length..]
            : "ws://" + (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                ? trimmed["http://".Length..]
                : trimmed);

        return new Uri($"{wsBase}/{path.TrimStart('/')}");
    }
}
