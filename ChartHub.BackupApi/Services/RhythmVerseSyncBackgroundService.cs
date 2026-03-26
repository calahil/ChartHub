using ChartHub.BackupApi.Options;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ChartHub.BackupApi.Services;

public sealed partial class RhythmVerseSyncBackgroundService(
    IRhythmVerseUpstreamClient upstreamClient,
    IServiceScopeFactory scopeFactory,
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

        using IServiceScope scope = scopeFactory.CreateScope();
        IRhythmVerseRepository repository = scope.ServiceProvider.GetRequiredService<IRhythmVerseRepository>();

        // Attempt to load and validate existing cursor for potential resume
        SyncCursor? cursor = await LoadOrInitializeCursorAsync(repository, cancellationToken)
            .ConfigureAwait(false);

        int page = 1;
        string reconciliationRunId;
        bool isResuming = false;
        DateTime cursorStartedUtc = DateTime.UtcNow;

        if (cursor is not null
            && cursor.IsValid(syncOptions.Value.RecordsPerPage, syncOptions.Value.CursorMaxAgeMinutes, DateTime.UtcNow))
        {
            // Cursor valid: resume from rewound page with same run ID
            reconciliationRunId = cursor.RunId;
            cursorStartedUtc = cursor.CursorStartedUtc;
            int rewindPages = Math.Min(cursor.LastCompletedPage - 1, syncOptions.Value.PageRewindOnResume);
            page = Math.Max(1, cursor.LastCompletedPage - rewindPages);
            isResuming = true;
            Log.SyncResuming(logger, page, reconciliationRunId, cursor.LastCompletedPage);
        }
        else
        {
            // Cursor invalid/absent: start fresh with new run ID
            reconciliationRunId = Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture);
            Log.SyncStartingFresh(logger, reconciliationRunId);
        }

        if (!isResuming)
        {
            await repository.BeginReconciliationRunAsync(reconciliationRunId, cancellationToken).ConfigureAwait(false);
        }

        string? rawWatermark = await repository
            .GetSyncStateAsync("sync.last_record_updated", cancellationToken)
            .ConfigureAwait(false);

        long? updatedSince = null;
        if (rawWatermark is not null && long.TryParse(rawWatermark, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long parsedWatermark))
        {
            updatedSince = parsedWatermark;
        }

        long? cycleHighWatermark = updatedSince;
        int maxPages = Math.Max(1, syncOptions.Value.MaxPagesPerRun);
        int records = Math.Clamp(syncOptions.Value.RecordsPerPage, 1, 250);
        bool cycleFailed = false;
        bool reachedTerminalPage = false;

        for (; page <= maxPages; page++)
        {
            Models.RhythmVersePageEnvelope? envelope = await FetchPageWithRetryAsync(page, records, updatedSince, cancellationToken)
                .ConfigureAwait(false);

            if (envelope is null)
            {
                cycleFailed = true;
                break;
            }

            await repository
                .SetSyncStateAsync("records.total_available", envelope.TotalAvailable.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken)
                .ConfigureAwait(false);

            IReadOnlyList<Models.SyncedSong> songs = upstreamClient.ConvertToSyncedSongs(envelope);

            if (songs.Count == 0)
            {
                reachedTerminalPage = true;
                break;
            }

            await repository.UpsertSongsAsync(songs, cancellationToken, reconciliationRunId).ConfigureAwait(false);

            // Advance the high watermark from this page's records.
            foreach (Models.SyncedSong song in songs)
            {
                if (song.RecordUpdatedUnix.HasValue
                    && song.RecordUpdatedUnix.Value > (cycleHighWatermark ?? 0L))
                {
                    cycleHighWatermark = song.RecordUpdatedUnix.Value;
                }
            }

            Log.SyncPageComplete(logger, page, songs.Count);

            // Persist page cursor for potential resume on next cycle
            await SaveCursorAsync(repository, reconciliationRunId, page, records, cursorStartedUtc, cancellationToken)
                .ConfigureAwait(false);

            if (songs.Count < records)
            {
                reachedTerminalPage = true;
                break;
            }
        }

        if (!cycleFailed && !reachedTerminalPage)
        {
            Log.SyncCycleIncomplete(logger, maxPages);
        }

        if (!cycleFailed && reachedTerminalPage)
        {
            await repository.FinalizeReconciliationRunAsync(reconciliationRunId, cancellationToken).ConfigureAwait(false);

            // Clear cursor after successful finalization
            await ClearCursorAsync(repository, cancellationToken)
                .ConfigureAwait(false);

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
                // Check if this is a non-transient error (client error 4xx)
                if (IsNonTransientError(ex))
                {
                    Log.SyncPageFailedNonTransient(logger, page, ex);
                    return null;
                }

                // Handle transient errors with retry logic
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

    /// <summary>
    /// Determines if an error is non-transient (should not be retried).
    /// Permanent client errors (4xx) are non-transient.
    /// Transient errors include network timeouts, server errors (5xx), and temporary failures.
    /// </summary>
    private static bool IsNonTransientError(Exception ex)
    {
        if (ex is HttpRequestException hre && hre.StatusCode.HasValue)
        {
            // 4xx client errors are non-transient
            return (int)hre.StatusCode >= 400 && (int)hre.StatusCode < 500;
        }

        return false;
    }

    private sealed record SyncCursor(
        string RunId,
        int LastCompletedPage,
        int RecordsPerPage,
        DateTime CursorStartedUtc)
    {
        public bool IsValid(int currentRecordsPerPage, int maxAgeMinutes, DateTime nowUtc)
        {
            if (RecordsPerPage != currentRecordsPerPage)
            {
                return false;
            }

            TimeSpan age = nowUtc - CursorStartedUtc;
            return age.TotalMinutes < maxAgeMinutes;
        }
    }

    private async Task<SyncCursor?> LoadOrInitializeCursorAsync(
        IRhythmVerseRepository repository,
        CancellationToken cancellationToken)
    {
        string? rawRunId = await repository
            .GetSyncStateAsync("sync.run_id", cancellationToken)
            .ConfigureAwait(false);

        string? rawLastPage = await repository
            .GetSyncStateAsync("sync.last_completed_page", cancellationToken)
            .ConfigureAwait(false);

        string? rawRecordsPerPage = await repository
            .GetSyncStateAsync("sync.records_per_page", cancellationToken)
            .ConfigureAwait(false);

        string? rawStartedUtc = await repository
            .GetSyncStateAsync("sync.cursor_started_utc", cancellationToken)
            .ConfigureAwait(false);

        string? rawStatus = await repository
            .GetSyncStateAsync("sync.status", cancellationToken)
            .ConfigureAwait(false);

        if (rawRunId is null
            || rawLastPage is null
            || rawRecordsPerPage is null
            || rawStartedUtc is null
            || rawStatus is null)
        {
            return null;
        }

        if (!int.TryParse(rawLastPage, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int lastPage))
        {
            return null;
        }

        if (!int.TryParse(rawRecordsPerPage, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int recordsPerPage))
        {
            return null;
        }

        if (!DateTime.TryParseExact(rawStartedUtc, "O", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime startedUtc))
        {
            return null;
        }

        if (rawStatus != "in_progress")
        {
            return null;
        }

        return new SyncCursor(rawRunId, lastPage, recordsPerPage, startedUtc);
    }

    private async Task SaveCursorAsync(
        IRhythmVerseRepository repository,
        string runId,
        int page,
        int recordsPerPage,
        DateTime cursorStartedUtc,
        CancellationToken cancellationToken)
    {
        await repository
            .SetSyncStateAsync("sync.run_id", runId, cancellationToken)
            .ConfigureAwait(false);

        await repository
            .SetSyncStateAsync("sync.last_completed_page", page.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken)
            .ConfigureAwait(false);

        await repository
            .SetSyncStateAsync("sync.records_per_page", recordsPerPage.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken)
            .ConfigureAwait(false);

        await repository
            .SetSyncStateAsync("sync.cursor_started_utc", cursorStartedUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture), cancellationToken)
            .ConfigureAwait(false);

        await repository
            .SetSyncStateAsync("sync.status", "in_progress", cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ClearCursorAsync(
        IRhythmVerseRepository repository,
        CancellationToken cancellationToken)
    {
        // Set status to completed so we don't attempt resume later
        await repository
            .SetSyncStateAsync("sync.status", "completed", cancellationToken)
            .ConfigureAwait(false);
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

        [LoggerMessage(EventId = 1007, Level = LogLevel.Error, Message = "Sync page {Page} encountered a non-transient error (client error); not retrying.")]
        public static partial void SyncPageFailedNonTransient(ILogger logger, int page, Exception exception);

        [LoggerMessage(EventId = 1008, Level = LogLevel.Warning, Message = "RhythmVerse sync cycle stopped before a terminal page was reached; reconciliation run left incomplete after {MaxPages} pages.")]
        public static partial void SyncCycleIncomplete(ILogger logger, int maxPages);

        [LoggerMessage(EventId = 1009, Level = LogLevel.Information, Message = "Resuming RhythmVerse sync from page {Page} (run {RunId}, rewound from {LastCompletedPage}).")]
        public static partial void SyncResuming(ILogger logger, int page, string runId, int lastCompletedPage);

        [LoggerMessage(EventId = 1010, Level = LogLevel.Information, Message = "Starting fresh RhythmVerse sync cycle (run {RunId}).")]
        public static partial void SyncStartingFresh(ILogger logger, string runId);
    }
}
