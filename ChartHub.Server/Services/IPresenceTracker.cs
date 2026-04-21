namespace ChartHub.Server.Services;

public interface IPresenceTracker
{
    bool IsAnyonePresent { get; }

    string? ConnectedDeviceName { get; }

    string? ConnectedUserEmail { get; }

    /// <summary>
    /// Registers a presence connection for the given device and user.
    /// Returns <c>false</c> if a different user already has the presence slot.
    /// </summary>
    bool Register(string deviceName, string userEmail);

    void Unregister(string deviceName);

    IAsyncEnumerable<PresenceUpdate> WatchAsync(CancellationToken cancellationToken);
}

public readonly record struct PresenceUpdate(bool IsPresent, string? DeviceName, string? UserEmail);
