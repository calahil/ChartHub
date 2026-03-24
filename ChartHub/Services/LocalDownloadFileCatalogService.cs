using ChartHub.Models;
using ChartHub.Utilities;

namespace ChartHub.Services;

public interface ILocalDownloadFileCatalogService
{
    Task<IReadOnlyList<WatcherFile>> GetFilesAsync(string rootDirectory, CancellationToken cancellationToken = default);
}

public sealed class LocalDownloadFileCatalogService : ILocalDownloadFileCatalogService
{
    public async Task<IReadOnlyList<WatcherFile>> GetFilesAsync(string rootDirectory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
        {
            return [];
        }

        string[] filePaths = Directory
            .EnumerateFiles(rootDirectory)
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .ToArray();

        var result = new List<WatcherFile>(filePaths.Length);
        foreach (string filePath in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                WatcherFileType fileType = await WatcherFileTypeResolver.GetFileTypeAsync(filePath).ConfigureAwait(false);
                string icon = WatcherFileTypeResolver.GetIconForFileType(fileType);

                var info = new FileInfo(filePath);
                long sizeBytes = info.Length;

                result.Add(new WatcherFile(
                    displayName: Path.GetFileName(filePath),
                    filePath: filePath,
                    watcherFileType: fileType,
                    imageFile: icon,
                    sizeBytes: sizeBytes));
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Sync", "Skipping local file during catalog refresh", new Dictionary<string, object?>
                {
                    ["path"] = filePath,
                    ["reason"] = ex.Message,
                });
            }
        }

        return result;
    }
}
