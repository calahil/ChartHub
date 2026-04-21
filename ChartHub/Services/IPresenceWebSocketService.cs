namespace ChartHub.Services;

/// <summary>
/// Maintains a persistent WebSocket connection to /api/v1/presence/ws while
/// the Android app is foregrounded. Closing the connection signals the server
/// that the user is gone, turning the HUD indicator red.
/// </summary>
public interface IPresenceWebSocketService : IDisposable
{
    /// <summary>Connects to the server presence endpoint. Safe to call multiple times.</summary>
    Task ConnectAsync(string baseUrl, string bearerToken, string deviceName, CancellationToken cancellationToken = default);

    /// <summary>Gracefully closes the presence connection.</summary>
    Task DisconnectAsync();
}
