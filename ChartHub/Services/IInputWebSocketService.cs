namespace ChartHub.Services;

public interface IInputWebSocketService : IDisposable
{
    bool IsConnected { get; }

    Task ConnectAsync(string baseUrl, string bearerToken, string path, string deviceName, CancellationToken cancellationToken = default);

    Task SendAsync<T>(T message, CancellationToken cancellationToken = default);

    Task DisconnectAsync();
}
