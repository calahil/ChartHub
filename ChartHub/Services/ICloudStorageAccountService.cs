namespace ChartHub.Services;

public interface ICloudStorageAccountService
{
    string ProviderId { get; }
    string ProviderDisplayName { get; }

    Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default);
    Task LinkAsync(CancellationToken cancellationToken = default);
    Task UnlinkAsync(CancellationToken cancellationToken = default);
}

public sealed class GoogleCloudStorageAccountService(IGoogleDriveClient googleDriveClient) : ICloudStorageAccountService
{
    public string ProviderId => "google-drive";
    public string ProviderDisplayName => "Google Drive";

    public Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default)
        => googleDriveClient.TryInitializeSilentAsync(cancellationToken);

    public Task LinkAsync(CancellationToken cancellationToken = default)
        => googleDriveClient.InitializeAsync(cancellationToken);

    public Task UnlinkAsync(CancellationToken cancellationToken = default)
        => googleDriveClient.SignOutAsync(cancellationToken);
}
