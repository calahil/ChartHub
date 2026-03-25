using ChartHub.BackupApi.Options;

using Microsoft.Extensions.Options;

namespace ChartHub.BackupApi.Services;

public sealed partial class RhythmVerseSyncBackgroundService(
    IRhythmVerseUpstreamClient upstreamClient,
    IRhythmVerseRepository repository,
    IOptions<SyncOptions> syncOptions,
    ILogger<RhythmVerseSyncBackgroundService> logger) : BackgroundService
{
    // 3 retry attempts after the initial try: delays of 5s, 15s, 30s.
    // Exposed as internal so tests can inject zero-length delays without waiting.
    internal TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!syncOptions.Value.Enabled)
        {
            Log.SyncDisabled(logger);
            return;
        }

        await RunSyncCycleAsync(stoppingToken).ConfigureAwait(false);

        using PeriodicTimer timer = new(TimeSpan.FromMinutes(Math.Max(1, syncOptions.Value.IntervalMinutes)));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await RunSyncCycleAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RunSyncCycleAsync(CancellationToken cancellationToken)
    {
        Log.SyncCycleStarted(logger);

        string? rawWatermark = await repository
            .GetSyncStateAsync("sync.last_record_updated", cancellationToken)
            .ConfigureAwait(false);

        long? updatedSince = null;
        if (rawWatermark is not null && long.TryParse(rawWatermark, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long parsedWatermark))
        {
            updatedSince = parsedWatermark;
        }

        long? cycleHighWatermark = updatedSince;
        int page = 1;
        int maxPages = Math.Max(1, syncOptions.Value.MaxPagesPerRun);
        int records = Math.Clamp(syncOptions.Value.RecordsPerPage, 1, 250);
        bool cycleCompleted = true;

        for (; page <= maxPages; page++)
        {
            Models.RhythmVersePageEnvelope? envelope = await FetchPageWithRetryAsync(page, records, updatedSince, cancellationToken)
                .ConfigureAwait(false);

            if (envelope is null)
            {
                cycleCompleted = false;
                break;
            }

            IReadOnlyList<Models.SyncedSong> songs = upstreamClient.ConvertToSyncedSongs(envelope);

            if (songs.Count == 0)
            {
                break;
            }

            await repository.UpsertSongsAsync(songs, cancellationToken).ConfigureAwait(false);

            // Advance the high watermark from this page's records.
            foreach (Models.SyncedSong song in songs)
            {
                if (song.RecordUpdatedUnix.HasValue
                    && song.RecordUpdatedUnix.Value > (cycleHighWatermark ?? 0L))
                {
                    cycleHighWatermark = song.RecordUpdatedUnix.Value;
                }
            }

            await repository
                .SetSyncStateAsync("records.total_available", envelope.TotalAvailable.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken)
                .ConfigureAwait(false);

            Log.SyncPageComplete(logger, page, songs.Count);

            if (songs.Count < records)
            {
                break;
            }
        }

        if (cycleCompleted)
        {
            await repository
                .SetSyncStateAsync("sync.last_success_utc", DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture), cancellationToken)
                .ConfigureAwait(false);
        }

        // Save the watermark even if the cycle had errors so future cycles only re-process
        // records from where the last successful upsert left off.
        if (cycleHighWatermark.HasValue && cycleHighWatermark.Value > (updatedSince ?? 0L))
        {
            await repository
                .SetSyncStateAsync("sync.last_record_updated", cycleHighWatermark.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken)
                .ConfigureAwait(false);

            Log.SyncWatermarkUpdated(logger, cycleHighWatermark.Value);
        }
    }

    private async Task<Models.RhythmVersePageEnvelope?> FetchPageWithRetryAsync(
        int page,
        int records,
        long? updatedSince,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt <= RetryDelays.Length; attempt++)
        {
            try
            {
                return await upstreamClient
                    .FetchSongsPageAsync(page, records, updatedSince, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (attempt < RetryDelays.Length)
                {
                    Log.SyncPageRetrying(logger, page, attempt + 1, (int)RetryDelays[attempt].TotalSeconds, ex);
                    await Task.Delay(RetryDelays[attempt], cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    Log.SyncPageFailed(logger, page, ex);
                    return null;
                }
            }
        }

        return null;
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "RhythmVerse sync is disabled.")]
        public static partial void SyncDisabled(ILogger logger);

        [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Starting RhythmVerse sync cycle.")]
        public static partial void SyncCycleStarted(ILogger logger);

        [LoggerMessage(EventId = 1003, Level = LogLevel.Error, Message = "Sync page {Page} failed after all retries.")]
        public static partial void SyncPageFailed(ILogger logger, int page, Exception exception);

        [LoggerMessage(EventId = 1004, Level = LogLevel.Information, Message = "Synced page {Page} with {Count} songs.")]
        public static partial void SyncPageComplete(ILogger logger, int page, int count);

        [LoggerMessage(EventId = 1005, Level = LogLevel.Warning, Message = "Sync page {Page} failed (attempt {Attempt}), retrying in {DelaySeconds}s.")]
        public static partial void SyncPageRetrying(ILogger logger, int page, int attempt, int delaySeconds, Exception exception);

        [LoggerMessage(EventId = 1006, Level = LogLevel.Information, Message = "Sync watermark advanced to {WatermarkUnix}.")]
        public static partial void SyncWatermarkUpdated(ILogger logger, long watermarkUnix);
    }
}
