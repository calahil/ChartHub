using System.Collections.ObjectModel;
using System.Diagnostics;
using ChartHub.Models;
using ChartHub.Utilities;

namespace ChartHub.Services.Transfers;

public sealed class TransferOrchestrator(
    AppGlobalSettings settings,
    DownloadService downloadService,
    IGoogleDriveClient googleDriveClient,
    ITransferSourceResolver sourceResolver,
    ILocalDestinationWriter localDestinationWriter,
    IGoogleDriveDestinationWriter googleDriveDestinationWriter) : ITransferOrchestrator
{
    private readonly AppGlobalSettings _settings = settings;
    private readonly DownloadService _downloadService = downloadService;
    private readonly IGoogleDriveClient _googleDriveClient = googleDriveClient;
    private readonly ITransferSourceResolver _sourceResolver = sourceResolver;
    private readonly ILocalDestinationWriter _localDestinationWriter = localDestinationWriter;
    private readonly IGoogleDriveDestinationWriter _googleDriveDestinationWriter = googleDriveDestinationWriter;

    public async Task<TransferResult> QueueSongDownloadAsync(
        ViewSong song,
        DownloadFile? downloadItem,
        ObservableCollection<DownloadFile> downloads,
        CancellationToken cancellationToken = default)
    {
        var transferId = CreateOperationId("trf");
        var transferStopwatch = Stopwatch.StartNew();
        var destination = OperatingSystem.IsAndroid()
            ? TransferDestinationKind.GoogleDrive
            : TransferDestinationKind.LocalStorage;

        var request = new TransferRequest(
            DisplayName: song.FileName ?? string.Empty,
            SourceUrl: song.DownloadLink ?? string.Empty,
            SourceFileSize: song.FileSize,
            Destination: destination);

        if (string.IsNullOrWhiteSpace(request.DisplayName) || string.IsNullOrWhiteSpace(request.SourceUrl))
        {
            var invalid = new DownloadFile(request.DisplayName, _settings.TempDir, request.SourceUrl, request.SourceFileSize)
            {
                Finished = true,
            };
            return new TransferResult(false, TransferStage.Failed, null, "Song metadata is missing file name or source URL.", invalid);
        }

        downloadItem ??= new DownloadFile(request.DisplayName, _settings.TempDir, request.SourceUrl, request.SourceFileSize);
        if (!downloads.Contains(downloadItem))
            downloads.Add(downloadItem);

        try
        {
            Logger.LogInfo("Transfer", "Transfer queued", new Dictionary<string, object?>
            {
                ["transferId"] = transferId,
                ["displayName"] = request.DisplayName,
                ["sourceUrl"] = request.SourceUrl,
                ["destination"] = request.Destination.ToString(),
            });

            SetStage(downloadItem, TransferStage.ResolvingSource, transferId, request.DisplayName, request.Destination);
            var source = await _sourceResolver.ResolveAsync(request.SourceUrl, cancellationToken);

            if (request.Destination == TransferDestinationKind.GoogleDrive)
            {
                var directCopyResult = await TryCopyDriveFileAsync(request, source, downloadItem, transferId, cancellationToken);
                if (directCopyResult is not null)
                {
                    Logger.LogInfo("Transfer", "Transfer completed via direct Drive copy", new Dictionary<string, object?>
                    {
                        ["transferId"] = transferId,
                        ["displayName"] = request.DisplayName,
                        ["destination"] = request.Destination.ToString(),
                        ["elapsedMs"] = transferStopwatch.ElapsedMilliseconds,
                    });
                    return directCopyResult;
                }
            }

            if (source.Kind == TransferSourceKind.GoogleDriveFolder && !string.IsNullOrWhiteSpace(source.DriveId))
            {
                SetStage(downloadItem, TransferStage.DownloadingFolder, transferId, request.DisplayName, request.Destination);
                downloadItem.DownloadProgress = 15;
                downloadItem.DisplayName = EnsureZipName(downloadItem.DisplayName);
                var folderZipPath = Path.Combine(downloadItem.FilePath, downloadItem.DisplayName);
                var stageProgress = new Progress<TransferProgressUpdate>(update =>
                {
                    SetStage(downloadItem, update.Stage, transferId, request.DisplayName, request.Destination);
                    if (update.ProgressPercent.HasValue)
                        downloadItem.DownloadProgress = Math.Max(downloadItem.DownloadProgress, update.ProgressPercent.Value);
                });

                await _googleDriveClient.DownloadFolderAsZipAsync(
                    source.DriveId,
                    folderZipPath,
                    stageProgress,
                    cancellationToken);
                downloadItem.DownloadProgress = 100;
                downloadItem.Finished = true;
            }
            else
            {
                SetStage(downloadItem, TransferStage.Downloading, transferId, request.DisplayName, request.Destination);
                await _downloadService.DownloadFileAsync(downloadItem, cancellationToken);
            }

            var tempPath = Path.Combine(downloadItem.FilePath, downloadItem.DisplayName);
            if (!File.Exists(tempPath))
                throw new FileNotFoundException("Downloaded file was not found in temp storage.", tempPath);

            if (request.Destination == TransferDestinationKind.LocalStorage)
            {
                SetStage(downloadItem, TransferStage.MovingToDestination, transferId, request.DisplayName, request.Destination);
                var localResult = await _localDestinationWriter.WriteFromTempAsync(
                    tempPath,
                    downloadItem.DisplayName,
                    cancellationToken);

                downloadItem.DisplayName = localResult.FinalName;
                downloadItem.FilePath = localResult.DestinationContainer;
                SetStage(downloadItem, TransferStage.Completed, transferId, request.DisplayName, request.Destination);

                Logger.LogInfo("Transfer", "Transfer completed to local storage", new Dictionary<string, object?>
                {
                    ["transferId"] = transferId,
                    ["displayName"] = downloadItem.DisplayName,
                    ["destination"] = request.Destination.ToString(),
                    ["finalLocation"] = localResult.FinalLocation,
                    ["elapsedMs"] = transferStopwatch.ElapsedMilliseconds,
                });

                return new TransferResult(true, TransferStage.Completed, localResult.FinalLocation, null, downloadItem);
            }

            SetStage(downloadItem, TransferStage.Uploading, transferId, request.DisplayName, request.Destination);
            var driveResult = await _googleDriveDestinationWriter.WriteFromTempAsync(
                tempPath,
                downloadItem.DisplayName,
                cancellationToken);
            File.Delete(tempPath);

            downloadItem.DisplayName = driveResult.FinalName;
            downloadItem.FilePath = driveResult.DestinationContainer;
            downloadItem.Finished = true;
            downloadItem.DownloadProgress = 100;
            SetStage(downloadItem, TransferStage.Completed, transferId, request.DisplayName, request.Destination);

            Logger.LogInfo("Transfer", "Transfer completed to Google Drive", new Dictionary<string, object?>
            {
                ["transferId"] = transferId,
                ["displayName"] = downloadItem.DisplayName,
                ["destination"] = request.Destination.ToString(),
                ["finalLocation"] = driveResult.FinalLocation,
                ["elapsedMs"] = transferStopwatch.ElapsedMilliseconds,
            });

            return new TransferResult(true, TransferStage.Completed, driveResult.FinalLocation, null, downloadItem);
        }
        catch (OperationCanceledException)
        {
            Logger.LogInfo("Transfer", "Transfer cancelled", new Dictionary<string, object?>
            {
                ["transferId"] = transferId,
                ["displayName"] = downloadItem.DisplayName,
                ["stage"] = downloadItem.Status,
                ["elapsedMs"] = transferStopwatch.ElapsedMilliseconds,
            });
            downloadItem.Finished = true;
            SetStage(downloadItem, TransferStage.Cancelled, transferId, request.DisplayName, request.Destination);
            downloadItem.ErrorMessage = "Transfer cancelled.";
            return new TransferResult(false, TransferStage.Cancelled, null, "Transfer cancelled.", downloadItem);
        }
        catch (Exception ex)
        {
            Logger.LogError("Transfer", "Transfer failed", ex, new Dictionary<string, object?>
            {
                ["transferId"] = transferId,
                ["displayName"] = downloadItem.DisplayName,
                ["stage"] = downloadItem.Status,
                ["sourceUrl"] = request.SourceUrl,
                ["destination"] = request.Destination.ToString(),
                ["elapsedMs"] = transferStopwatch.ElapsedMilliseconds,
            });
            downloadItem.Finished = true;
            SetStage(downloadItem, TransferStage.Failed, transferId, request.DisplayName, request.Destination);
            const string userError = "Transfer failed. See logs for details.";
            downloadItem.ErrorMessage = userError;
            return new TransferResult(false, TransferStage.Failed, null, userError, downloadItem);
        }
    }

    private async Task<TransferResult?> TryCopyDriveFileAsync(
        TransferRequest request,
        ResolvedTransferSource source,
        DownloadFile downloadItem,
        string transferId,
        CancellationToken cancellationToken)
    {
        var copyStopwatch = Stopwatch.StartNew();
        if (source.Kind != TransferSourceKind.GoogleDriveFile || string.IsNullOrWhiteSpace(source.DriveId))
            return null;
        var sourceFileId = source.DriveId;

        try
        {
            SetStage(downloadItem, TransferStage.CopyingInGoogleDrive, transferId, request.DisplayName, request.Destination);
            var copyResult = await _googleDriveDestinationWriter.TryCopyDriveFileAsync(
                sourceFileId,
                request.DisplayName,
                cancellationToken);

            if (copyResult is null)
                return null;

            downloadItem.DisplayName = copyResult.FinalName;
            downloadItem.FilePath = copyResult.DestinationContainer;
            downloadItem.DownloadProgress = 100;
            downloadItem.Finished = true;
            SetStage(downloadItem, TransferStage.Completed, transferId, request.DisplayName, request.Destination);

            Logger.LogInfo("Transfer", "Drive copy-first completed", new Dictionary<string, object?>
            {
                ["transferId"] = transferId,
                ["displayName"] = downloadItem.DisplayName,
                ["driveFileId"] = sourceFileId,
                ["elapsedMs"] = copyStopwatch.ElapsedMilliseconds,
            });

            return new TransferResult(true, TransferStage.Completed, copyResult.FinalLocation, null, downloadItem);
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Transfer", "Drive copy-first fell back to download/upload", new Dictionary<string, object?>
            {
                ["transferId"] = transferId,
                ["displayName"] = request.DisplayName,
                ["driveFileId"] = sourceFileId,
                ["reason"] = ex.GetType().Name,
                ["elapsedMs"] = copyStopwatch.ElapsedMilliseconds,
            });
            Logger.LogError("Transfer", "Drive copy-first failed before fallback", ex, new Dictionary<string, object?>
            {
                ["transferId"] = transferId,
                ["displayName"] = request.DisplayName,
                ["driveFileId"] = sourceFileId,
                ["elapsedMs"] = copyStopwatch.ElapsedMilliseconds,
            });
            return null;
        }
    }

    public async Task<IReadOnlyList<string>> DownloadSelectedCloudFilesToLocalAsync(
        IEnumerable<WatcherFile> selectedCloudFiles,
        CancellationToken cancellationToken = default)
    {
        var transferId = CreateOperationId("sync");
        var selected = selectedCloudFiles.Where(file => !string.IsNullOrWhiteSpace(file.FilePath)).ToList();
        var stopwatch = Stopwatch.StartNew();
        Logger.LogInfo("Transfer", "Cloud-to-local selected download started", new Dictionary<string, object?>
        {
            ["transferId"] = transferId,
            ["fileCount"] = selected.Count,
        });

        await _googleDriveClient.InitializeAsync(cancellationToken);
        var downloaded = new List<string>();

        foreach (var file in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var localName = _localDestinationWriter.ResolveUniqueName(file.DisplayName);

            var localPath = Path.Combine(_settings.DownloadDir, localName);
            await _googleDriveClient.DownloadFileAsync(file.FilePath, localPath);
            downloaded.Add(localPath);
        }

        Logger.LogInfo("Transfer", "Cloud-to-local selected download completed", new Dictionary<string, object?>
        {
            ["transferId"] = transferId,
            ["requestedCount"] = selected.Count,
            ["downloadedCount"] = downloaded.Count,
            ["elapsedMs"] = stopwatch.ElapsedMilliseconds,
        });

        return downloaded;
    }

    public async Task<IReadOnlyList<string>> SyncCloudToLocalAdditiveAsync(
        IEnumerable<WatcherFile> currentCloudFiles,
        CancellationToken cancellationToken = default)
    {
        var transferId = CreateOperationId("sync");
        var cloudFiles = currentCloudFiles.Where(file => !string.IsNullOrWhiteSpace(file.FilePath)).ToList();
        var stopwatch = Stopwatch.StartNew();
        Logger.LogInfo("Transfer", "Cloud-to-local additive sync started", new Dictionary<string, object?>
        {
            ["transferId"] = transferId,
            ["cloudFileCount"] = cloudFiles.Count,
        });

        await _googleDriveClient.InitializeAsync(cancellationToken);
        var downloaded = new List<string>();

        foreach (var cloudFile in cloudFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var localName = _localDestinationWriter.ResolveUniqueName(cloudFile.DisplayName);
            var localPath = Path.Combine(_settings.DownloadDir, localName);
            if (File.Exists(localPath))
                continue;

            await _googleDriveClient.DownloadFileAsync(cloudFile.FilePath, localPath);
            downloaded.Add(localPath);
        }

        Logger.LogInfo("Transfer", "Cloud-to-local additive sync completed", new Dictionary<string, object?>
        {
            ["transferId"] = transferId,
            ["cloudFileCount"] = cloudFiles.Count,
            ["downloadedCount"] = downloaded.Count,
            ["elapsedMs"] = stopwatch.ElapsedMilliseconds,
        });

        return downloaded;
    }

    private static void SetStage(
        DownloadFile downloadItem,
        TransferStage stage,
        string transferId,
        string displayName,
        TransferDestinationKind destination)
    {
        var previousStage = downloadItem.Status;
        var nextStage = stage.ToString();
        downloadItem.Status = nextStage;

        if (string.Equals(previousStage, nextStage, StringComparison.Ordinal))
            return;

        Logger.LogInfo("Transfer", "Transfer stage changed", new Dictionary<string, object?>
        {
            ["transferId"] = transferId,
            ["displayName"] = displayName,
            ["destination"] = destination.ToString(),
            ["previousStage"] = string.IsNullOrWhiteSpace(previousStage) ? "none" : previousStage,
            ["stage"] = nextStage,
        });
    }

    private static string CreateOperationId(string prefix)
    {
        return $"{prefix}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
    }

    private static string EnsureZipName(string fileName)
    {
        return fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : $"{fileName}.zip";
    }
}
