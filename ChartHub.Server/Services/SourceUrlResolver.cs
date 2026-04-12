using System.Text.Json;

using ChartHub.Server.Options;

using Microsoft.Extensions.Options;

namespace ChartHub.Server.Services;

public sealed class SourceUrlResolver(
    IHttpClientFactory httpClientFactory,
    IOptions<GoogleDriveOptions> googleDriveOptions) : ISourceUrlResolver
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient("downloads");
    private readonly GoogleDriveOptions _googleDriveOptions = googleDriveOptions.Value;

    public async Task<ResolvedSourceUrl> ResolveAsync(string sourceUrl, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out Uri? candidate)
            || (candidate.Scheme != Uri.UriSchemeHttp && candidate.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("SourceUrl must be an absolute HTTP/HTTPS URL.");
        }

        string? driveFileId = TryExtractGoogleDriveFileId(candidate);
        string? driveFolderId = TryExtractGoogleDriveFolderId(candidate);

        if (string.IsNullOrWhiteSpace(driveFileId) && string.IsNullOrWhiteSpace(driveFolderId))
        {
            return new ResolvedSourceUrl(candidate);
        }

        if (string.IsNullOrWhiteSpace(_googleDriveOptions.ApiKey))
        {
            throw new InvalidOperationException("GoogleDrive:ApiKey is required for Google Drive sources.");
        }

        string driveItemId = driveFolderId ?? driveFileId!;
        GoogleDriveMetadata metadata = await GetGoogleDriveMetadataAsync(driveItemId, cancellationToken).ConfigureAwait(false);
        if (metadata is null || string.IsNullOrWhiteSpace(metadata.Id))
        {
            throw new InvalidOperationException("Failed to resolve Google Drive file metadata.");
        }

        if (string.Equals(metadata.MimeType, "application/vnd.google-apps.folder", StringComparison.OrdinalIgnoreCase))
        {
            string folderSuggestedName = EnsureZipExtension(metadata.Name ?? "google-drive-folder");
            return new ResolvedSourceUrl(null, folderSuggestedName, metadata.Id);
        }

        Uri mediaUri = new($"https://www.googleapis.com/drive/v3/files/{metadata.Id}?alt=media&key={Uri.EscapeDataString(_googleDriveOptions.ApiKey)}");
        return new ResolvedSourceUrl(mediaUri, metadata.Name);
    }

    private async Task<GoogleDriveMetadata> GetGoogleDriveMetadataAsync(string driveItemId, CancellationToken cancellationToken)
    {
        Uri metadataUri = new($"https://www.googleapis.com/drive/v3/files/{driveItemId}?fields=id,name,mimeType&key={Uri.EscapeDataString(_googleDriveOptions.ApiKey)}");
        using HttpResponseMessage metadataResponse = await _httpClient.GetAsync(metadataUri, cancellationToken).ConfigureAwait(false);
        metadataResponse.EnsureSuccessStatusCode();

        await using Stream metadataStream = await metadataResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        GoogleDriveMetadata? metadata = await JsonSerializer.DeserializeAsync<GoogleDriveMetadata>(
                metadataStream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken)
            .ConfigureAwait(false);

        return metadata ?? throw new InvalidOperationException("Failed to resolve Google Drive file metadata.");
    }

    public static string? TryExtractGoogleDriveFileId(Uri uri)
    {
        if (!string.Equals(uri.Host, "drive.google.com", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Host, "www.drive.google.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        int fileMarker = Array.FindIndex(segments, segment => string.Equals(segment, "d", StringComparison.OrdinalIgnoreCase));
        if (fileMarker >= 0 && fileMarker + 1 < segments.Length)
        {
            return segments[fileMarker + 1];
        }

        string query = uri.Query;
        if (query.StartsWith("?", StringComparison.Ordinal))
        {
            query = query[1..];
        }

        foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && string.Equals(parts[0], "id", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }

    public static string? TryExtractGoogleDriveFolderId(Uri uri)
    {
        if (!string.Equals(uri.Host, "drive.google.com", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Host, "www.drive.google.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        int folderMarker = Array.FindIndex(segments, segment => string.Equals(segment, "folders", StringComparison.OrdinalIgnoreCase));
        if (folderMarker >= 0 && folderMarker + 1 < segments.Length)
        {
            return segments[folderMarker + 1];
        }

        string query = uri.Query;
        if (query.StartsWith("?", StringComparison.Ordinal))
        {
            query = query[1..];
        }

        foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && string.Equals(parts[0], "id", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }

    private static string EnsureZipExtension(string value)
    {
        if (string.IsNullOrWhiteSpace(Path.GetExtension(value)))
        {
            return value + ".zip";
        }

        return value;
    }

    private sealed class GoogleDriveMetadata
    {
        public string? Id { get; init; }

        public string? Name { get; init; }

        public string? MimeType { get; init; }
    }
}
