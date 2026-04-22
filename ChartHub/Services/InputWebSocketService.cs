using System.Net.WebSockets;
using System.Text.Json;

namespace ChartHub.Services;

public sealed class InputWebSocketService : IInputWebSocketService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _wsGate = new(1, 1);
    private ClientWebSocket? _ws;
    private bool _disposed;

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public async Task ConnectAsync(string baseUrl, string bearerToken, string path, string deviceName, CancellationToken cancellationToken = default)
    {
        await _wsGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _ws?.Dispose();

            ClientWebSocket next = new();
            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                next.Options.SetRequestHeader("Authorization", $"Bearer {bearerToken}");
            }

            if (!string.IsNullOrWhiteSpace(deviceName))
            {
                next.Options.SetRequestHeader("X-Device-Name", deviceName);
            }

            Uri wsUri = BuildWebSocketUri(baseUrl, path);
            await next.ConnectAsync(wsUri, cancellationToken).ConfigureAwait(false);
            _ws = next;
        }
        finally
        {
            _wsGate.Release();
        }
    }

    public async Task SendAsync<T>(T message, CancellationToken cancellationToken = default)
    {
        await _wsGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
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
        finally
        {
            _wsGate.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _wsGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
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
        finally
        {
            _wsGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _wsGate.Wait();
        try
        {
            _ws?.Dispose();
            _ws = null;
        }
        finally
        {
            _wsGate.Release();
            _wsGate.Dispose();
        }
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
