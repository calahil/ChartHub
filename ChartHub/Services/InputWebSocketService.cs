using System.Net.WebSockets;
using System.Text.Json;

namespace ChartHub.Services;

public sealed class InputWebSocketService : IInputWebSocketService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private ClientWebSocket? _ws;
    private bool _disposed;

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public async Task ConnectAsync(string baseUrl, string bearerToken, string path, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _ws?.Dispose();
        _ws = new ClientWebSocket();

        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            _ws.Options.SetRequestHeader("Authorization", $"Bearer {bearerToken}");
        }

        Uri wsUri = BuildWebSocketUri(baseUrl, path);
        await _ws.ConnectAsync(wsUri, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendAsync<T>(T message, CancellationToken cancellationToken = default)
    {
        if (_ws is null || _ws.State != WebSocketState.Open)
        {
            return;
        }

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        await _ws.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task DisconnectAsync()
    {
        if (_ws is null || _ws.State != WebSocketState.Open)
        {
            return;
        }

        try
        {
            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "client disconnected", CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (WebSocketException)
        {
            // Connection may have already been terminated.
        }
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
        // Derive ws:// or wss:// from the http/https base URL.
        string trimmed = baseUrl.TrimEnd('/');
        string wsBase = trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? "wss://" + trimmed["https://".Length..]
            : "ws://" + (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                ? trimmed["http://".Length..]
                : trimmed);

        string pathTrimmed = path.TrimStart('/');
        return new Uri($"{wsBase}/{pathTrimmed}");
    }
}
