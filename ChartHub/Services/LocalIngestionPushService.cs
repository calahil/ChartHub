using ChartHub.Models;
using ChartHub.Utilities;

namespace ChartHub.Services;

public interface ILocalIngestionPushService
{
    Task<long> PushAsync(string baseUrl, string token, LocalIngestionEntry entry, CancellationToken cancellationToken = default);
}

public sealed class LocalIngestionPushService(IDesktopSyncApiClient desktopSyncApiClient) : ILocalIngestionPushService
{
    private readonly IDesktopSyncApiClient _desktopSyncApiClient = desktopSyncApiClient;

    public async Task<long> PushAsync(string baseUrl, string token, LocalIngestionEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (string.IsNullOrWhiteSpace(entry.LocalPath))
        {
            throw new InvalidOperationException("Local file path is required.");
        }

        if (!File.Exists(entry.LocalPath))
        {
            throw new InvalidOperationException("Local ingestion file does not exist.");
        }

        string displayName = string.IsNullOrWhiteSpace(entry.DisplayName)
            ? SafePathHelper.SanitizeFileName(Path.GetFileName(entry.LocalPath), "upload.bin")
            : SafePathHelper.SanitizeFileName(entry.DisplayName, "upload.bin");

        LocalIngestionUploadMetadata metadata = new(
            Source: entry.Source,
            SourceId: entry.SourceId,
            SourceLink: entry.SourceLink,
            Artist: entry.Artist,
            Title: entry.Title,
            Charter: entry.Charter,
            LibrarySource: entry.LibrarySource);

        return await _desktopSyncApiClient.UploadIngestionFileAsync(
            baseUrl,
            token,
            entry.LocalPath,
            displayName,
            metadata,
            cancellationToken).ConfigureAwait(false);
    }
}