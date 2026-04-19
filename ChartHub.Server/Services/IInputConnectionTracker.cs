namespace ChartHub.Server.Services;

public record HudStatusUpdate(int ConnectedDeviceCount, string? DeviceName);

public interface IInputConnectionTracker
{
    int ActiveConnectionCount { get; }

    /// <summary>
    /// Attempts to register a connection for <paramref name="deviceName"/>.
    /// Returns <c>false</c> if a different device already holds the single connection slot.
    /// </summary>
    bool RegisterConnection(string deviceName);

    void UnregisterConnection(string deviceName);

    IAsyncEnumerable<HudStatusUpdate> WatchAsync(CancellationToken cancellationToken);
}
