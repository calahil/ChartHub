namespace ChartHub.Server.Services;

public interface IInputConnectionTracker
{
    int ActiveConnectionCount { get; }

    void RegisterConnection();

    void UnregisterConnection();

    IAsyncEnumerable<int> WatchAsync(CancellationToken cancellationToken);
}
