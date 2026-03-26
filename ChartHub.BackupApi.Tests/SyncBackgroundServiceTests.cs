using System.Text.Json.Nodes;

using ChartHub.BackupApi.Models;
using ChartHub.BackupApi.Options;
using ChartHub.BackupApi.Services;
using ChartHub.BackupApi.Tests.TestInfrastructure;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ChartHub.BackupApi.Tests;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class SyncBackgroundServiceTests
{
    [Fact]
    public async Task ExecuteAsync_WhenSyncDisabled_DoesNotFetchFromUpstream()
    {
        FakeUpstreamClient upstream = new([]);
        FakeRepository repo = new();
        RhythmVerseSyncBackgroundService sut = BuildService(
            new SyncOptions { Enabled = false }, upstream, repo);

        using CancellationTokenSource cts = new();
        await sut.StartAsync(cts.Token);
        await cts.CancelAsync();

        Assert.Equal(0, upstream.FetchCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithPartialFinalPage_UpsertsAllSongsAndWritesWatermark()
    {
        // page 1 is full (2 == RecordsPerPage) → continues; page 2 is short (1 < 2) → stops
        SyncedSong songA = TestSong(1, 100L);
        SyncedSong songB = TestSong(2, 200L);
        SyncedSong songC = TestSong(3, 150L);

        FakeUpstreamClient upstream = new([[songA, songB], [songC]]);
        FakeRepository repo = new();
        RhythmVerseSyncBackgroundService sut = BuildService(
            new SyncOptions { Enabled = true, RecordsPerPage = 2, MaxPagesPerRun = 10 },
            upstream, repo);
        sut.RetryDelays = [];

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        await sut.StartAsync(cts.Token);
        await repo.SuccessWrittenTask.WaitAsync(cts.Token);
        await cts.CancelAsync();

        Assert.Equal(3, repo.UpsertedSongs.Count);
        Assert.Equal(200L, repo.WatermarkWritten);
        Assert.NotNull(repo.LastSuccessUtc);
        Assert.Single(repo.FinalizedRunIds);
        Assert.Single(repo.BegunRunIds);
        Assert.Equal(repo.BegunRunIds[0], repo.FinalizedRunIds[0]);
        Assert.Equal(repo.BegunRunIds[0], repo.StateValues["reconciliation.current_run_id"]);
        Assert.NotNull(repo.StateValues["reconciliation.started_utc"]);
        Assert.NotNull(repo.StateValues["reconciliation.completed_utc"]);
        Assert.Equal(repo.LastSuccessUtc, repo.StateValues["sync.last_success_utc"]);
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyFirstPage_WritesSuccessAndNoUpserts()
    {
        FakeUpstreamClient upstream = new([[]]);
        FakeRepository repo = new();
        RhythmVerseSyncBackgroundService sut = BuildService(
            new SyncOptions { Enabled = true, RecordsPerPage = 10, MaxPagesPerRun = 10 },
            upstream, repo);
        sut.RetryDelays = [];

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        await sut.StartAsync(cts.Token);
        await repo.SuccessWrittenTask.WaitAsync(cts.Token);
        await cts.CancelAsync();

        Assert.Empty(repo.UpsertedSongs);
        Assert.NotNull(repo.LastSuccessUtc);
        Assert.Null(repo.WatermarkWritten); // no songs → no watermark advance
        Assert.Single(repo.FinalizedRunIds);
    }

    [Fact]
    public async Task ExecuteAsync_WhenFirstAttemptFails_RetriesAndSucceeds()
    {
        SyncedSong song = TestSong(1, 50L);
        FakeUpstreamClient upstream = new([[song]], failFirstNAttempts: 1);
        FakeRepository repo = new();
        RhythmVerseSyncBackgroundService sut = BuildService(
            new SyncOptions { Enabled = true, RecordsPerPage = 10, MaxPagesPerRun = 10 },
            upstream, repo);
        sut.RetryDelays = [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero];

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        await sut.StartAsync(cts.Token);
        await repo.SuccessWrittenTask.WaitAsync(cts.Token);
        await cts.CancelAsync();

        Assert.Single(repo.UpsertedSongs);
        Assert.NotNull(repo.LastSuccessUtc);
        // Retry happened: FetchCallCount > 1
        Assert.True(upstream.FetchCallCount >= 2);
        Assert.Single(repo.FinalizedRunIds);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAllRetriesExhausted_CycleEndsGracefully()
    {
        // alwaysFail + 3 retry delays = 1 + 3 = 4 total calls before giving up
        FakeUpstreamClient upstream = new([], alwaysFail: true, signalAfterCalls: 4);
        FakeRepository repo = new();
        RhythmVerseSyncBackgroundService sut = BuildService(
            new SyncOptions { Enabled = true, RecordsPerPage = 10, MaxPagesPerRun = 10 },
            upstream, repo);
        sut.RetryDelays = [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero];

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        await sut.StartAsync(cts.Token);
        await upstream.DoneTask.WaitAsync(cts.Token);
        await cts.CancelAsync();

        Assert.Equal(4, upstream.FetchCallCount);
        Assert.Empty(repo.UpsertedSongs);
        Assert.Null(repo.LastSuccessUtc); // cycle was incomplete
        Assert.Empty(repo.FinalizedRunIds);
    }

    [Fact]
    public async Task ExecuteAsync_WhenClientErrorFails_DoesNotRetry()
    {
        // Simulate a HTTP 405 Method Not Allowed (non-transient client error)
        FakeUpstreamClient upstream = new([], clientErrorStatusCode: 405, signalAfterCalls: 1);
        FakeRepository repo = new();
        RhythmVerseSyncBackgroundService sut = BuildService(
            new SyncOptions { Enabled = true, RecordsPerPage = 10, MaxPagesPerRun = 10 },
            upstream, repo);
        sut.RetryDelays = [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero];

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        await sut.StartAsync(cts.Token);
        await upstream.DoneTask.WaitAsync(cts.Token);
        await cts.CancelAsync();

        // Should only attempt once (no retries for 4xx)
        Assert.Equal(1, upstream.FetchCallCount);
        Assert.Empty(repo.UpsertedSongs);
        Assert.Empty(repo.FinalizedRunIds);
    }

    [Fact]
    public async Task ExecuteAsync_WhenFirstAttemptsFailTransient_RetriesThenSucceeds()
    {
        // Simulate transient failures (408 Timeout) that recover on retry
        SyncedSong song = TestSong(1, 50L);
        FakeUpstreamClient upstream = new([[song]], failFirstNAttempts: 2, transientFailure: true);
        FakeRepository repo = new();
        RhythmVerseSyncBackgroundService sut = BuildService(
            new SyncOptions { Enabled = true, RecordsPerPage = 10, MaxPagesPerRun = 10 },
            upstream, repo);
        sut.RetryDelays = [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero];

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        await sut.StartAsync(cts.Token);
        await repo.SuccessWrittenTask.WaitAsync(cts.Token);
        await cts.CancelAsync();

        // Should have retried after transient failures
        Assert.True(upstream.FetchCallCount >= 3);
        Assert.Single(repo.UpsertedSongs);
        Assert.NotNull(repo.LastSuccessUtc);
        Assert.Single(repo.FinalizedRunIds);
    }

    [Fact]
    public async Task ExecuteAsync_WhenMaxPagesReached_DoesNotFinalizeReconciliation()
    {
        SyncedSong songA = TestSong(1, 100L);
        SyncedSong songB = TestSong(2, 200L);
        SyncedSong songC = TestSong(3, 300L);

        FakeUpstreamClient upstream = new([[songA], [songB], [songC]]);
        FakeRepository repo = new();
        RhythmVerseSyncBackgroundService sut = BuildService(
            new SyncOptions { Enabled = true, RecordsPerPage = 1, MaxPagesPerRun = 2 },
            upstream, repo);
        sut.RetryDelays = [];

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        await sut.StartAsync(cts.Token);
        await Task.Delay(100, cts.Token);
        await cts.CancelAsync();

        Assert.Equal(2, repo.UpsertedSongs.Count);
        Assert.Null(repo.LastSuccessUtc);
        Assert.Empty(repo.FinalizedRunIds);
        Assert.Single(repo.BegunRunIds);
        Assert.Equal(repo.BegunRunIds[0], repo.StateValues["reconciliation.current_run_id"]);
        Assert.NotNull(repo.StateValues["reconciliation.started_utc"]);
        Assert.False(repo.StateValues.ContainsKey("reconciliation.completed_utc"));
    }

    [Fact]
    public async Task ExecuteAsync_WithValidCursor_CompletesSuccessfully()
    {
        // Simply verify that with valid cursor setup, cycle completes
        SyncedSong songPage1 = TestSong(10, 100L);

        FakeUpstreamClient upstream = new([[songPage1]]);
        FakeRepository repo = new();

        // Set up valid cursor state (just needed to verify it doesn't crash)
        await repo.SetSyncStateAsync("sync.run_id", Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture), CancellationToken.None);
        await repo.SetSyncStateAsync("sync.last_completed_page", "1", CancellationToken.None);
        await repo.SetSyncStateAsync("sync.records_per_page", "10", CancellationToken.None);
        await repo.SetSyncStateAsync("sync.cursor_started_utc", DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture), CancellationToken.None);
        await repo.SetSyncStateAsync("sync.status", "in_progress", CancellationToken.None);

        RhythmVerseSyncBackgroundService sut = BuildService(
            new SyncOptions { Enabled = true, RecordsPerPage = 10, MaxPagesPerRun = 10, CursorMaxAgeMinutes = 120, PageRewindOnResume = 2 },
            upstream, repo);
        sut.RetryDelays = [];

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        await sut.StartAsync(cts.Token);
        await repo.SuccessWrittenTask.WaitAsync(cts.Token);
        await cts.CancelAsync();

        // Core verification: cycle completed successfully
        Assert.Single(repo.UpsertedSongs);
        Assert.NotNull(repo.LastSuccessUtc);
        Assert.Single(repo.FinalizedRunIds);
    }

    [Fact]
    public async Task ExecuteAsync_WithStaleCursor_DiscardsAndStartsFresh()
    {
        // Setup: cursor is old (3 hours old), max age is 120 min → should discard and start fresh
        SyncedSong songPage1 = TestSong(10, 100L);

        FakeUpstreamClient upstream = new([[songPage1]]);
        FakeRepository repo = new();

        // Pre-populate with stale cursor
        string staleCursorRunId = Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture);
        DateTime staleCursorStartedUtc = DateTime.UtcNow.AddHours(-3);
        await repo.SetSyncStateAsync("sync.run_id", staleCursorRunId, CancellationToken.None);
        await repo.SetSyncStateAsync("sync.last_completed_page", "1", CancellationToken.None);
        await repo.SetSyncStateAsync("sync.records_per_page", "10", CancellationToken.None);
        await repo.SetSyncStateAsync("sync.cursor_started_utc", staleCursorStartedUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture), CancellationToken.None);
        await repo.SetSyncStateAsync("sync.status", "in_progress", CancellationToken.None);

        RhythmVerseSyncBackgroundService sut = BuildService(
            new SyncOptions { Enabled = true, RecordsPerPage = 10, MaxPagesPerRun = 10, CursorMaxAgeMinutes = 120, PageRewindOnResume = 2 },
            upstream, repo);
        sut.RetryDelays = [];

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        await sut.StartAsync(cts.Token);
        await repo.SuccessWrittenTask.WaitAsync(cts.Token);
        await cts.CancelAsync();

        // Should have discarded stale cursor and started fresh with new run ID
        Assert.NotEqual(staleCursorRunId, repo.BegunRunIds.FirstOrDefault());
        Assert.Single(repo.BegunRunIds); // Fresh start
        Assert.Single(repo.UpsertedSongs);
        Assert.NotNull(repo.LastSuccessUtc);
    }

    [Fact]
    public async Task ExecuteAsync_WithConfiguredRecordsPerPageChanged_DiscardsAndStartsFresh()
    {
        // Setup: cursor has records_per_page=100, but config now says 50 → discard and start fresh
        SyncedSong songPage1 = TestSong(10, 100L);

        FakeUpstreamClient upstream = new([[songPage1]]);
        FakeRepository repo = new();

        // Pre-populate cursor with different records_per_page
        string cursorRunId = Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture);
        DateTime cursorStartedUtc = DateTime.UtcNow;
        await repo.SetSyncStateAsync("sync.run_id", cursorRunId, CancellationToken.None);
        await repo.SetSyncStateAsync("sync.last_completed_page", "1", CancellationToken.None);
        await repo.SetSyncStateAsync("sync.records_per_page", "100", CancellationToken.None);
        await repo.SetSyncStateAsync("sync.cursor_started_utc", cursorStartedUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture), CancellationToken.None);
        await repo.SetSyncStateAsync("sync.status", "in_progress", CancellationToken.None);

        RhythmVerseSyncBackgroundService sut = BuildService(
            new SyncOptions { Enabled = true, RecordsPerPage = 50, MaxPagesPerRun = 10, CursorMaxAgeMinutes = 120, PageRewindOnResume = 2 },
            upstream, repo);
        sut.RetryDelays = [];

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        await sut.StartAsync(cts.Token);
        await repo.SuccessWrittenTask.WaitAsync(cts.Token);
        await cts.CancelAsync();

        // Should have discarded cursor due to config mismatch and started fresh
        Assert.NotEqual(cursorRunId, repo.BegunRunIds.FirstOrDefault());
        Assert.Single(repo.BegunRunIds);
        Assert.Single(repo.UpsertedSongs);
        Assert.NotNull(repo.LastSuccessUtc);
    }

    [Fact]
    public async Task ExecuteAsync_CursorStatusSavedAfterPageUpsert()
    {
        // Verify that cursor status is persisted as "in_progress" after pages are upserted
        SyncedSong songPage1 = TestSong(10, 100L);
        SyncedSong songPage2 = TestSong(20, 200L);

        FakeUpstreamClient upstream = new([[songPage1], [songPage2]]);
        FakeRepository repo = new();

        RhythmVerseSyncBackgroundService sut = BuildService(
            new SyncOptions { Enabled = true, RecordsPerPage = 1, MaxPagesPerRun = 10, CursorMaxAgeMinutes = 120, PageRewindOnResume = 1 },
            upstream, repo);
        sut.RetryDelays = [];

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        await sut.StartAsync(cts.Token);
        await repo.SuccessWrittenTask.WaitAsync(cts.Token);
        await cts.CancelAsync();

        // Verify cycle completed and cursor status transitioned to "completed"
        Assert.NotNull(repo.LastSuccessUtc);
        Assert.Single(repo.FinalizedRunIds);
        Assert.Equal("completed", repo.StateValues["sync.status"]); // Cursor status set to completed after finalize
    }

    private static RhythmVerseSyncBackgroundService BuildService(
        SyncOptions options,
        IRhythmVerseUpstreamClient upstream,
        IRhythmVerseRepository repo)
    {
        IServiceScopeFactory scopeFactory = new FakeScopeFactory(repo);

        return new(
            upstream,
            scopeFactory,
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<RhythmVerseSyncBackgroundService>.Instance);
    }

    private sealed class FakeScopeFactory(IRhythmVerseRepository repository) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new FakeScope(repository);
    }

    private sealed class FakeScope(IRhythmVerseRepository repository) : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = new FakeServiceProvider(repository);

        public void Dispose()
        {
        }
    }

    private sealed class FakeServiceProvider(IRhythmVerseRepository repository) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IRhythmVerseRepository))
            {
                return repository;
            }

            throw new InvalidOperationException($"No service registered for type '{serviceType.FullName}'.");
        }
    }

    private static SyncedSong TestSong(long id, long? recordUpdatedUnix = null)
    {
        string songJson = recordUpdatedUnix.HasValue
            ? System.Text.Json.JsonSerializer.Serialize(
                new { data = new { song_id = id, record_updated = recordUpdatedUnix.Value }, file = new { } })
            : System.Text.Json.JsonSerializer.Serialize(
                new { data = new { song_id = id }, file = new { } });
        string dataJson = recordUpdatedUnix.HasValue
            ? System.Text.Json.JsonSerializer.Serialize(
                new { song_id = id, record_updated = recordUpdatedUnix.Value })
            : System.Text.Json.JsonSerializer.Serialize(
                new { song_id = id });

        return new SyncedSong
        {
            SongId = id,
            RecordId = $"record-{id}",
            RecordUpdatedUnix = recordUpdatedUnix,
            SongJson = songJson,
            DataJson = dataJson,
            FileJson = "{}",
        };
    }

    private sealed class FakeUpstreamClient(
        IReadOnlyList<IReadOnlyList<SyncedSong>> pages,
        int failFirstNAttempts = 0,
        bool alwaysFail = false,
        int signalAfterCalls = 0,
        int? clientErrorStatusCode = null,
        bool transientFailure = false) : IRhythmVerseUpstreamClient
    {
        private readonly TaskCompletionSource _done =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int FetchCallCount { get; private set; }
        public Task DoneTask => _done.Task;

        public Task<RhythmVersePageEnvelope> FetchSongsPageAsync(
            int page, int records, long? updatedSince, CancellationToken cancellationToken)
        {
            int call = FetchCallCount++;

            if (signalAfterCalls > 0 && FetchCallCount >= signalAfterCalls)
            {
                _done.TrySetResult();
            }

            // Simulate client error (4xx) - non-transient, should not retry
            if (clientErrorStatusCode.HasValue)
            {
                throw new HttpRequestException(
                    $"Simulated client error",
                    null,
                    (System.Net.HttpStatusCode)clientErrorStatusCode.Value);
            }

            if (alwaysFail || call < failFirstNAttempts)
            {
                if (transientFailure)
                {
                    // Simulate transient error (5xx or timeout)
                    throw new HttpRequestException(
                        $"Simulated transient failure (call {call})",
                        null,
                        System.Net.HttpStatusCode.InternalServerError);
                }
                else
                {
                    throw new HttpRequestException($"Simulated upstream failure (call {call})");
                }
            }

            int pageIndex = page - 1;
            IReadOnlyList<SyncedSong> pageSongs = pageIndex < pages.Count
                ? pages[pageIndex]
                : (IReadOnlyList<SyncedSong>)[];

            var songNodes = pageSongs
                .Select(s => (JsonNode?)JsonNode.Parse(s.SongJson))
                .ToList();

            return Task.FromResult(new RhythmVersePageEnvelope
            {
                TotalAvailable = pages.SelectMany(p => p).Count(),
                TotalFiltered = pages.SelectMany(p => p).Count(),
                Returned = pageSongs.Count,
                Start = (page - 1) * records,
                Records = records,
                Page = page,
                Songs = songNodes,
            });
        }

        public IReadOnlyList<SyncedSong> ConvertToSyncedSongs(RhythmVersePageEnvelope envelope)
        {
            List<SyncedSong> result = [];

            foreach (JsonNode? node in envelope.Songs)
            {
                if (node is null)
                {
                    continue;
                }

                long id = (long?)node["data"]?["song_id"] ?? 0L;
                long? updated = (long?)node["data"]?["record_updated"];

                result.Add(new SyncedSong
                {
                    SongId = id,
                    RecordId = $"record-{id}",
                    RecordUpdatedUnix = updated,
                    SongJson = node.ToJsonString(),
                    DataJson = node["data"]?.ToJsonString() ?? "{}",
                    FileJson = node["file"]?.ToJsonString() ?? "{}",
                });
            }

            return result;
        }
    }

    private sealed class FakeRepository : IRhythmVerseRepository
    {
        private readonly TaskCompletionSource _successWritten =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly List<SyncedSong> _upsertedSongs = [];
        private readonly Dictionary<string, string> _stateValues = new(StringComparer.Ordinal);
        private readonly object _lock = new();

        public List<SyncedSong> UpsertedSongs
        {
            get
            {
                lock (_lock)
                {
                    return [.. _upsertedSongs];
                }
            }
        }

        public long? WatermarkWritten { get; private set; }
        public string? LastSuccessUtc { get; private set; }
        public List<string> BegunRunIds { get; } = [];
        public List<string> FinalizedRunIds { get; } = [];
        public IReadOnlyDictionary<string, string> StateValues => _stateValues;
        public Task SuccessWrittenTask => _successWritten.Task;

        public Task BeginReconciliationRunAsync(string reconciliationRunId, CancellationToken cancellationToken)
        {
            BegunRunIds.Add(reconciliationRunId);
            _stateValues["reconciliation.current_run_id"] = reconciliationRunId;
            _stateValues["reconciliation.started_utc"] = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            return Task.CompletedTask;
        }

        public Task UpsertSongsAsync(
            IEnumerable<SyncedSong> songs,
            CancellationToken cancellationToken,
            string? reconciliationRunId = null)
        {
            lock (_lock)
            {
                _upsertedSongs.AddRange(songs);
            }

            return Task.CompletedTask;
        }

        public Task FinalizeReconciliationRunAsync(string reconciliationRunId, CancellationToken cancellationToken)
        {
            FinalizedRunIds.Add(reconciliationRunId);
            _stateValues["reconciliation.current_run_id"] = reconciliationRunId;
            _stateValues["reconciliation.completed_utc"] = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            return Task.CompletedTask;
        }

        public Task<RhythmVersePageEnvelope> GetSongsPageAsync(
            int page, int records, string? query, string? genre, string? gameformat,
            string? author, string? group, CancellationToken cancellationToken)
            => Task.FromResult(new RhythmVersePageEnvelope
            {
                TotalAvailable = 0,
                TotalFiltered = 0,
                Returned = 0,
                Start = 0,
                Records = records,
                Page = page,
                Songs = [],
            });

        public Task<RhythmVersePageEnvelope> GetSongsPageAdvancedAsync(
            int page,
            int records,
            string? query,
            string? genre,
            string? gameformat,
            string? author,
            string? group,
            string? sortBy,
            string? sortOrder,
            IReadOnlyList<string>? instruments,
            CancellationToken cancellationToken)
            => GetSongsPageAsync(page, records, query, genre, gameformat, author, group, cancellationToken);

        public Task<string?> GetDownloadUrlByFileIdAsync(string fileId, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

        public Task<JsonNode?> GetSongByIdAsync(long songId, CancellationToken cancellationToken)
            => Task.FromResult<JsonNode?>(null);

        public Task SetSyncStateAsync(string key, string value, CancellationToken cancellationToken)
        {
            _stateValues[key] = value;

            if (key == "sync.last_success_utc")
            {
                LastSuccessUtc = value;
                _successWritten.TrySetResult();
            }
            else if (key == "sync.last_record_updated"
                     && long.TryParse(
                         value,
                         System.Globalization.NumberStyles.Integer,
                         System.Globalization.CultureInfo.InvariantCulture,
                         out long watermark))
            {
                WatermarkWritten = watermark;
            }

            return Task.CompletedTask;
        }

        public Task<string?> GetSyncStateAsync(string key, CancellationToken cancellationToken)
            => Task.FromResult(_stateValues.TryGetValue(key, out string? value) ? value : null);
    }
}
