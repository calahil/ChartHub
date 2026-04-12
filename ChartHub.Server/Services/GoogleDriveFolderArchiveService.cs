using System.IO.Compression;
using System.Text.Json;

using ChartHub.Server.Options;

using Microsoft.Extensions.Options;

namespace ChartHub.Server.Services;

public interface IGoogleDriveFolderArchiveService
{
    Task<string> DownloadFolderAsZipAsync(
        string folderId,
        string suggestedName,
        string downloadsDirectory,
        Guid jobId,
        CancellationToken cancellationToken);
}

public sealed class GoogleDriveFolderArchiveService(
    IHttpClientFactory httpClientFactory,
    IOptions<GoogleDriveOptions> googleDriveOptions) : IGoogleDriveFolderArchiveService
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient("downloads");
    private readonly GoogleDriveOptions _googleDriveOptions = googleDriveOptions.Value;

    public async Task<string> DownloadFolderAsZipAsync(
        string folderId,
        string suggestedName,
        string downloadsDirectory,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_googleDriveOptions.ApiKey))
        {
            throw new InvalidOperationException("GoogleDrive:ApiKey is required for Google Drive folder downloads.");
        }

        Directory.CreateDirectory(downloadsDirectory);

        string safeBaseName = MakeSafeFileName(Path.GetFileNameWithoutExtension(suggestedName));
        string destinationPath = Path.Combine(downloadsDirectory, $"{safeBaseName}-{jobId:D}.zip");
        string tempPath = destinationPath + ".tmp";

        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        await using (FileStream output = new(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, true))
        await using (ZipArchive zip = new(output, ZipArchiveMode.Create, leaveOpen: false))
        {
            await AddFolderToArchiveAsync(folderId, prefix: string.Empty, zip, cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        File.Move(tempPath, destinationPath);
        return destinationPath;
    }

    private async Task AddFolderToArchiveAsync(string folderId, string prefix, ZipArchive zip, CancellationToken cancellationToken)
    {
        string? pageToken = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            GoogleDriveFilesListResponse page = await ListFolderChildrenAsync(folderId, pageToken, cancellationToken).ConfigureAwait(false);

            foreach (GoogleDriveFileItem item in page.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string safeName = MakeSafeEntryName(item.Name);
                string entryPath = string.IsNullOrWhiteSpace(prefix) ? safeName : $"{prefix}/{safeName}";

                if (string.Equals(item.MimeType, "application/vnd.google-apps.folder", StringComparison.OrdinalIgnoreCase))
                {
                    await AddFolderToArchiveAsync(item.Id, entryPath, zip, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (item.MimeType.StartsWith("application/vnd.google-apps.", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Unsupported Google Workspace file type '{item.MimeType}' in folder archive download.");
                }

                ZipArchiveEntry zipEntry = zip.CreateEntry(entryPath, CompressionLevel.Fastest);
                await using Stream zipEntryStream = zipEntry.Open();
                await DownloadDriveFileToStreamAsync(item.Id, zipEntryStream, cancellationToken).ConfigureAwait(false);
            }

            pageToken = page.NextPageToken;
        }
        while (!string.IsNullOrWhiteSpace(pageToken));
    }

    private async Task DownloadDriveFileToStreamAsync(string fileId, Stream destination, CancellationToken cancellationToken)
    {
        Uri mediaUri = new($"https://www.googleapis.com/drive/v3/files/{fileId}?alt=media&key={Uri.EscapeDataString(_googleDriveOptions.ApiKey)}");
        using HttpResponseMessage response = await _httpClient
            .GetAsync(mediaUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    private async Task<GoogleDriveFilesListResponse> ListFolderChildrenAsync(string folderId, string? pageToken, CancellationToken cancellationToken)
    {
        string query = Uri.EscapeDataString($"'{folderId}' in parents and trashed=false");
        string pageTokenPart = string.IsNullOrWhiteSpace(pageToken)
            ? string.Empty
            : $"&pageToken={Uri.EscapeDataString(pageToken)}";

        Uri uri = new(
            $"https://www.googleapis.com/drive/v3/files?q={query}&fields=nextPageToken,files(id,name,mimeType)&pageSize=1000&includeItemsFromAllDrives=true&supportsAllDrives=true&key={Uri.EscapeDataString(_googleDriveOptions.ApiKey)}{pageTokenPart}");

        using HttpResponseMessage response = await _httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        GoogleDriveFilesListResponse? parsed = await JsonSerializer.DeserializeAsync<GoogleDriveFilesListResponse>(
                body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken)
            .ConfigureAwait(false);

        return parsed ?? new GoogleDriveFilesListResponse();
    }

    private static string MakeSafeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "download" : cleaned;
    }

    private static string MakeSafeEntryName(string value)
    {
        string withoutSeparators = value.Replace('/', '-').Replace('\\', '-');
        return MakeSafeFileName(withoutSeparators);
    }

    private sealed class GoogleDriveFilesListResponse
    {
        public string? NextPageToken { get; init; }

        public List<GoogleDriveFileItem> Files { get; init; } = [];
    }

    private sealed class GoogleDriveFileItem
    {
        public string Id { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public string MimeType { get; init; } = string.Empty;
    }
}
