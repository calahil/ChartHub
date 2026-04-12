namespace ChartHub.Server.Services;

public interface ISourceUrlResolver
{
    Task<ResolvedSourceUrl> ResolveAsync(string sourceUrl, CancellationToken cancellationToken);
}

public sealed record ResolvedSourceUrl(
    Uri? DownloadUri,
    string? SuggestedName = null,
    string? GoogleDriveFolderId = null)
{
    public bool IsGoogleDriveFolder => !string.IsNullOrWhiteSpace(GoogleDriveFolderId);
}
