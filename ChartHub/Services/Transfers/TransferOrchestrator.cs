using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;

using ChartHub.Models;
using ChartHub.Utilities;

namespace ChartHub.Services.Transfers;

public sealed class TransferOrchestrator(
    AppGlobalSettings settings,
    DownloadService downloadService,
    IGoogleDriveClient googleDriveClient,
    ITransferSourceResolver sourceResolver,
    ILocalDestinationWriter localDestinationWriter,
    SongIngestionCatalogService ingestionCatalog,
    SongIngestionStateMachine ingestionStateMachine) : ITransferOrchestrator
{
    private const int MinSongDownloadConcurrency = 1;
    private const int MaxSongDownloadConcurrency = 8;

    private readonly AppGlobalSettings _settings = settings;
    private readonly DownloadService _downloadService = downloadService;
    private readonly IGoogleDriveClient _googleDriveClient = googleDriveClient;
    private readonly ITransferSourceResolver _sourceResolver = sourceResolver;
    private readonly ILocalDestinationWriter _localDestinationWriter = localDestinationWriter;
    private readonly SongIngestionCatalogService _ingestionCatalog = ingestionCatalog;
    private readonly SongIngestionStateMachine _ingestionStateMachine = ingestionStateMachine;
    private static readonly TimeSpan DownloadRetryDelay = TimeSpan.FromSeconds(2);
    private readonly object _songDownloadConcurrencySync = new();
    private SemaphoreSlim _songDownloadConcurrencyGate = new(
        NormalizeSongDownloadConcurrency(settings.TransferOrchestratorConcurrencyCap),
        NormalizeSongDownloadConcurrency(settings.TransferOrchestratorConcurrencyCap));
    private int _songDownloadConcurrencyLimit = NormalizeSongDownloadConcurrency(settings.TransferOrchestratorConcurrencyCap);

    public async Task<TransferResult> QueueSongDownloadAsync(
        ViewSong song,
        DownloadFile? downloadItem,
        ObservableCollection<DownloadFile> downloads,
        CancellationToken cancellationToken = default)
    {
        string transferId = CreateOperationId("trf");
        var transferStopwatch = Stopwatch.StartNew();
        TransferDestinationKind destination = TransferDestinationKind.LocalStorage;

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
        {
            downloads.Add(downloadItem);
        }

        SemaphoreSlim songDownloadGate = GetSongDownloadConcurrencyGate();
        await songDownloadGate.WaitAsync(cancellationToken);

        try
        {
            string sourceName = NormalizeSourceName(song.SourceName);
            string canonicalSourceId = LibraryIdentityService.NormalizeSourceKey(sourceName, song.SourceId);
            SongIngestionRecord ingestion = await _ingestionCatalog.GetOrCreateIngestionAsync(
                sourceName,
                canonicalSourceId,
                request.SourceUrl,
                song.Artist,
                song.Title,
                song.Author?.Shortname,
                cancellationToken);
            SongIngestionAttemptRecord attempt = await _ingestionCatalog.StartAttemptAsync(ingestion.Id, cancellationToken);
            IngestionState ingestionState = ingestion.CurrentState;

            if (ingestionState != IngestionState.Queued)
            {
                ingestionState = await TransitionIngestionStateAsync(
                    ingestion.Id,
                    attempt.Id,
                    ingestionState,
                    IngestionState.Queued,
                    BuildTransitionDetails(transferId, "Attempt reset to queued", 0),
                    cancellationToken);
            }

            try
            {
                Logger.LogInfo("Transfer", "Transfer queued", new Dictionary<string, object?>
                {
                    ["transferId"] = transferId,
                    ["displayName"] = request.DisplayName,
                    ["sourceUrl"] = request.SourceUrl,
                    ["destination"] = request.Destination.ToString(),
                });

                ingestionState = await SetStageAsync(
                    downloadItem,
                    TransferStage.ResolvingSource,
                    transferId,
                    request.DisplayName,
                    request.Destination,
                    ingestion.Id,
                    attempt.Id,
                    ingestionState,
                    cancellationToken);
                ResolvedTransferSource source = await _sourceResolver.ResolveAsync(request.SourceUrl, cancellationToken);

                if (source.Kind == TransferSourceKind.GoogleDriveFolder && !string.IsNullOrWhiteSpace(source.DriveId))
                {
                    ingestionState = await SetStageAsync(
                        downloadItem,
                        TransferStage.DownloadingFolder,
                        transferId,
                        request.DisplayName,
                        request.Destination,
                        ingestion.Id,
                        attempt.Id,
                        ingestionState,
                        cancellationToken);
                    downloadItem.DownloadProgress = 15;
                    downloadItem.DisplayName = EnsureZipName(downloadItem.DisplayName);
                    string folderZipPath = Path.Combine(downloadItem.FilePath, downloadItem.DisplayName);
                    var stageProgress = new Progress<TransferProgressUpdate>(update =>
                    {
                        SetStage(downloadItem, update.Stage, transferId, request.DisplayName, request.Destination);
                        if (update.ProgressPercent.HasValue)
                        {
                            downloadItem.DownloadProgress = Math.Max(downloadItem.DownloadProgress, update.ProgressPercent.Value);
                        }
                    });

                    await ExecuteDownloadWithRetriesAsync(
                        async retryCount =>
                        {
                            await _googleDriveClient.DownloadFolderAsZipAsync(
                                source.DriveId,
                                folderZipPath,
                                stageProgress,
                                cancellationToken);
                        },
                        async retryCount =>
                        {
                            await _ingestionCatalog.RecordStateTransitionAsync(
                                ingestion.Id,
                                attempt.Id,
                                ingestionState,
                                ingestionState,
                                BuildTransitionDetails(transferId, "Retrying folder download", retryCount),
                                cancellationToken);
                        },
                        cancellationToken);

                    downloadItem.DownloadProgress = 100;
                    downloadItem.Finished = true;
                }
                else
                {
                    ingestionState = await SetStageAsync(
                        downloadItem,
                        TransferStage.Downloading,
                        transferId,
                        request.DisplayName,
                        request.Destination,
                        ingestion.Id,
                        attempt.Id,
                        ingestionState,
                        cancellationToken);

                    await ExecuteDownloadWithRetriesAsync(
                        async retryCount => await _downloadService.DownloadFileAsync(downloadItem, cancellationToken),
                        async retryCount =>
                        {
                            await _ingestionCatalog.RecordStateTransitionAsync(
                                ingestion.Id,
                                attempt.Id,
                                ingestionState,
                                ingestionState,
                                BuildTransitionDetails(transferId, "Retrying download", retryCount),
                                cancellationToken);
                        },
                        cancellationToken);
                }

                string tempPath = Path.Combine(downloadItem.FilePath, downloadItem.DisplayName);
                if (!File.Exists(tempPath))
                {
                    throw new FileNotFoundException("Downloaded file was not found in temp storage.", tempPath);
                }

                if (request.Destination == TransferDestinationKind.LocalStorage)
                {
                    ingestionState = await SetStageAsync(
                        downloadItem,
                        TransferStage.MovingToDestination,
                        transferId,
                        request.DisplayName,
                        request.Destination,
                        ingestion.Id,
                        attempt.Id,
                        ingestionState,
                        cancellationToken);
                    DestinationWriteResult localResult = await _localDestinationWriter.WriteFromTempAsync(
                        tempPath,
                        downloadItem.DisplayName,
                        cancellationToken);

                    downloadItem.DisplayName = localResult.FinalName;
                    downloadItem.FilePath = localResult.DestinationContainer;
                    ingestionState = await SetStageAsync(
                        downloadItem,
                        TransferStage.Completed,
                        transferId,
                        request.DisplayName,
                        request.Destination,
                        ingestion.Id,
                        attempt.Id,
                        ingestionState,
                        cancellationToken);

                    await _ingestionCatalog.UpsertAssetAsync(new SongIngestionAssetEntry(
                        IngestionId: ingestion.Id,
                        AttemptId: attempt.Id,
                        AssetRole: IngestionAssetRole.Downloaded,
                        Location: localResult.FinalLocation,
                        SizeBytes: request.SourceFileSize,
                        ContentHash: null,
                        RecordedAtUtc: DateTimeOffset.UtcNow),
                        cancellationToken);

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

                throw new NotSupportedException($"Transfer destination '{request.Destination}' is not supported.");
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
                await TransitionIngestionStateAsync(
                    ingestion.Id,
                    attempt.Id,
                    ingestionState,
                    IngestionState.Cancelled,
                    BuildTransitionDetails(transferId, "Transfer cancelled", 0),
                    cancellationToken);
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
                await TransitionIngestionStateAsync(
                    ingestion.Id,
                    attempt.Id,
                    ingestionState,
                    IngestionState.Failed,
                    BuildTransitionDetails(transferId, ex.Message, SongIngestionRetryPolicy.MaxDownloadRetries),
                    cancellationToken);
                const string userError = "Transfer failed. See logs for details.";
                downloadItem.ErrorMessage = userError;
                return new TransferResult(false, TransferStage.Failed, null, userError, downloadItem);
            }
        }
        finally
        {
            songDownloadGate.Release();
        }
    }

    private SemaphoreSlim GetSongDownloadConcurrencyGate()
    {
        int desiredLimit = NormalizeSongDownloadConcurrency(_settings.TransferOrchestratorConcurrencyCap);
        if (desiredLimit == _songDownloadConcurrencyLimit)
        {
            return _songDownloadConcurrencyGate;
        }

        lock (_songDownloadConcurrencySync)
        {
            if (desiredLimit == _songDownloadConcurrencyLimit)
            {
                return _songDownloadConcurrencyGate;
            }

            _songDownloadConcurrencyGate = new SemaphoreSlim(desiredLimit, desiredLimit);
            _songDownloadConcurrencyLimit = desiredLimit;
            Logger.LogInfo("Transfer", "Updated transfer concurrency cap", new Dictionary<string, object?>
            {
                ["concurrencyCap"] = desiredLimit,
            });

            return _songDownloadConcurrencyGate;
        }
    }

    private static int NormalizeSongDownloadConcurrency(int value)
    {
        if (value < MinSongDownloadConcurrency)
        {
            return MinSongDownloadConcurrency;
        }

        if (value > MaxSongDownloadConcurrency)
        {
            return MaxSongDownloadConcurrency;
        }

        return value;
    }


    private async Task ExecuteDownloadWithRetriesAsync(
        Func<int, Task> downloadAction,
        Func<int, Task> onRetry,
        CancellationToken cancellationToken)
    {
        int retryCount = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await downloadAction(retryCount);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception) when (SongIngestionRetryPolicy.CanRetryDownloadFailure(retryCount))
            {
                retryCount++;
                await onRetry(retryCount);
                await Task.Delay(DownloadRetryDelay, cancellationToken);
            }
        }
    }

    private async Task<IngestionState> SetStageAsync(
        DownloadFile downloadItem,
        TransferStage stage,
        string transferId,
        string displayName,
        TransferDestinationKind destination,
        long ingestionId,
        long attemptId,
        IngestionState currentState,
        CancellationToken cancellationToken)
    {
        SetStage(downloadItem, stage, transferId, displayName, destination);

        if (!TryMapTransferStage(stage, out IngestionState targetState))
        {
            return currentState;
        }

        return await TransitionIngestionStateAsync(
            ingestionId,
            attemptId,
            currentState,
            targetState,
            BuildTransitionDetails(transferId, $"Stage={stage}", 0),
            cancellationToken);
    }

    private async Task<IngestionState> TransitionIngestionStateAsync(
        long ingestionId,
        long attemptId,
        IngestionState currentState,
        IngestionState targetState,
        string detailsJson,
        CancellationToken cancellationToken)
    {
        if (currentState == targetState)
        {
            return currentState;
        }

        if (_ingestionStateMachine.CanTransition(currentState, targetState))
        {
            await _ingestionCatalog.RecordStateTransitionAsync(
                ingestionId,
                attemptId,
                currentState,
                targetState,
                detailsJson,
                cancellationToken);
            return targetState;
        }

        if (_ingestionStateMachine.CanTransition(currentState, IngestionState.Queued)
            && _ingestionStateMachine.CanTransition(IngestionState.Queued, targetState))
        {
            await _ingestionCatalog.RecordStateTransitionAsync(
                ingestionId,
                attemptId,
                currentState,
                IngestionState.Queued,
                BuildTransitionDetails("transition-reset", "Resetting state for a new attempt", 0),
                cancellationToken);

            await _ingestionCatalog.RecordStateTransitionAsync(
                ingestionId,
                attemptId,
                IngestionState.Queued,
                targetState,
                detailsJson,
                cancellationToken);

            return targetState;
        }

        Logger.LogWarning("Transfer", "Ingestion state transition skipped due to invalid path", new Dictionary<string, object?>
        {
            ["ingestionId"] = ingestionId,
            ["attemptId"] = attemptId,
            ["fromState"] = currentState.ToString(),
            ["toState"] = targetState.ToString(),
        });

        return currentState;
    }

    private static bool TryMapTransferStage(TransferStage stage, out IngestionState ingestionState)
    {
        ingestionState = stage switch
        {
            TransferStage.Queued => IngestionState.Queued,
            TransferStage.ResolvingSource => IngestionState.ResolvingSource,
            TransferStage.DownloadingFolder => IngestionState.Downloading,
            TransferStage.ZippingFolder => IngestionState.Downloading,
            TransferStage.Downloading => IngestionState.Downloading,
            TransferStage.MovingToDestination => IngestionState.Staged,
            TransferStage.Uploading => IngestionState.Downloading,
            TransferStage.Completed => IngestionState.Downloaded,
            TransferStage.Cancelled => IngestionState.Cancelled,
            TransferStage.Failed => IngestionState.Failed,
            _ => IngestionState.Failed,
        };

        return stage is not TransferStage.Cancelling;
    }

    private static string BuildTransitionDetails(string transferId, string message, int retryCount)
    {
        return JsonSerializer.Serialize(new
        {
            transferId,
            message,
            retryCount,
        });
    }

    private static string NormalizeSourceName(string? sourceName)
    {
        return LibrarySourceNames.NormalizeTrustedSource(sourceName, nameof(sourceName));
    }

    private static void SetStage(
        DownloadFile downloadItem,
        TransferStage stage,
        string transferId,
        string displayName,
        TransferDestinationKind destination)
    {
        string previousStage = downloadItem.Status;
        string nextStage = stage.ToString();
        downloadItem.Status = nextStage;

        if (string.Equals(previousStage, nextStage, StringComparison.Ordinal))
        {
            return;
        }

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
