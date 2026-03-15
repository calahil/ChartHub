using System.Collections.ObjectModel;
using RhythmVerseClient.Models;
using RhythmVerseClient.Utilities;

namespace RhythmVerseClient.Services.Transfers;

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
            downloadItem.Status = TransferStage.ResolvingSource.ToString();
            var source = await _sourceResolver.ResolveAsync(request.SourceUrl, cancellationToken);

            if (request.Destination == TransferDestinationKind.GoogleDrive)
            {
                var directCopyResult = await TryCopyDriveFileAsync(request, source, downloadItem, cancellationToken);
                if (directCopyResult is not null)
                    return directCopyResult;
            }

            if (source.Kind == TransferSourceKind.GoogleDriveFolder && !string.IsNullOrWhiteSpace(source.DriveId))
            {
                downloadItem.Status = TransferStage.DownloadingFolder.ToString();
                downloadItem.DownloadProgress = 15;
                downloadItem.DisplayName = EnsureZipName(downloadItem.DisplayName);
                var folderZipPath = Path.Combine(downloadItem.FilePath, downloadItem.DisplayName);
                var stageProgress = new Progress<TransferProgressUpdate>(update =>
                {
                    downloadItem.Status = update.Stage.ToString();
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
                downloadItem.Status = TransferStage.Downloading.ToString();
                await _downloadService.DownloadFileAsync(downloadItem, cancellationToken);
            }

            var tempPath = Path.Combine(downloadItem.FilePath, downloadItem.DisplayName);
            if (!File.Exists(tempPath))
                throw new FileNotFoundException("Downloaded file was not found in temp storage.", tempPath);

            if (request.Destination == TransferDestinationKind.LocalStorage)
            {
                downloadItem.Status = TransferStage.MovingToDestination.ToString();
                var localResult = await _localDestinationWriter.WriteFromTempAsync(
                    tempPath,
                    downloadItem.DisplayName,
                    cancellationToken);

                downloadItem.DisplayName = localResult.FinalName;
                downloadItem.FilePath = localResult.DestinationContainer;
                downloadItem.Status = TransferStage.Completed.ToString();

                return new TransferResult(true, TransferStage.Completed, localResult.FinalLocation, null, downloadItem);
            }

            downloadItem.Status = TransferStage.Uploading.ToString();
            var driveResult = await _googleDriveDestinationWriter.WriteFromTempAsync(
                tempPath,
                downloadItem.DisplayName,
                cancellationToken);
            File.Delete(tempPath);

            downloadItem.DisplayName = driveResult.FinalName;
            downloadItem.FilePath = driveResult.DestinationContainer;
            downloadItem.Finished = true;
            downloadItem.DownloadProgress = 100;
            downloadItem.Status = TransferStage.Completed.ToString();

            return new TransferResult(true, TransferStage.Completed, driveResult.FinalLocation, null, downloadItem);
        }
        catch (OperationCanceledException)
        {
            downloadItem.Finished = true;
            downloadItem.Status = TransferStage.Cancelled.ToString();
            downloadItem.ErrorMessage = "Transfer cancelled.";
            return new TransferResult(false, TransferStage.Cancelled, null, "Transfer cancelled.", downloadItem);
        }
        catch (Exception ex)
        {
            Logger.LogMessage($"Transfer failed: {ex.Message}");
            downloadItem.Finished = true;
            downloadItem.Status = TransferStage.Failed.ToString();
            downloadItem.ErrorMessage = ex.Message;
            return new TransferResult(false, TransferStage.Failed, null, ex.Message, downloadItem);
        }
    }

    private async Task<TransferResult?> TryCopyDriveFileAsync(
        TransferRequest request,
        ResolvedTransferSource source,
        DownloadFile downloadItem,
        CancellationToken cancellationToken)
    {
        if (source.Kind != TransferSourceKind.GoogleDriveFile || string.IsNullOrWhiteSpace(source.DriveId))
            return null;
        var sourceFileId = source.DriveId;

        try
        {
            downloadItem.Status = TransferStage.CopyingInGoogleDrive.ToString();
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
            downloadItem.Status = TransferStage.Completed.ToString();

            return new TransferResult(true, TransferStage.Completed, copyResult.FinalLocation, null, downloadItem);
        }
        catch (Exception ex)
        {
            Logger.LogMessage($"Drive copy-first fallback to download/upload: {ex.Message}");
            return null;
        }
    }

    public async Task<IReadOnlyList<string>> DownloadSelectedCloudFilesToLocalAsync(
        IEnumerable<WatcherFile> selectedCloudFiles,
        CancellationToken cancellationToken = default)
    {
        await _googleDriveClient.InitializeAsync(cancellationToken);
        var downloaded = new List<string>();

        foreach (var file in selectedCloudFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(file.FilePath))
                continue;

            var localName = _localDestinationWriter.ResolveUniqueName(file.DisplayName);

            var localPath = Path.Combine(_settings.DownloadDir, localName);
            await _googleDriveClient.DownloadFileAsync(file.FilePath, localPath);
            downloaded.Add(localPath);
        }

        return downloaded;
    }

    public async Task<IReadOnlyList<string>> SyncCloudToLocalAdditiveAsync(
        IEnumerable<WatcherFile> currentCloudFiles,
        CancellationToken cancellationToken = default)
    {
        await _googleDriveClient.InitializeAsync(cancellationToken);
        var downloaded = new List<string>();

        foreach (var cloudFile in currentCloudFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(cloudFile.FilePath))
                continue;

            var localPath = Path.Combine(_settings.DownloadDir, cloudFile.DisplayName);
            if (File.Exists(localPath))
                continue;

            await _googleDriveClient.DownloadFileAsync(cloudFile.FilePath, localPath);
            downloaded.Add(localPath);
        }

        return downloaded;
    }

    private static string EnsureZipName(string fileName)
    {
        return fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : $"{fileName}.zip";
    }
}
