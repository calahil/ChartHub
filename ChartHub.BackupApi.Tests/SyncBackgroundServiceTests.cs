using System.Text.Json.Nodes;

using ChartHub.BackupApi.Models;
using ChartHub.BackupApi.Options;
using ChartHub.BackupApi.Services;
using ChartHub.BackupApi.Tests.TestInfrastructure;

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
    }

    private static RhythmVerseSyncBackgroundService BuildService(
        SyncOptions options,
        IRhythmVerseUpstreamClient upstream,
        IRhythmVerseRepository repo) =>
        new(
            upstream,
            repo,
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<RhythmVerseSyncBackgroundService>.Instance);

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
        int signalAfterCalls = 0) : IRhythmVerseUpstreamClient
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

            if (alwaysFail || call < failFirstNAttempts)
            {
                throw new HttpRequestException($"Simulated upstream failure (call {call})");
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
        public Task SuccessWrittenTask => _successWritten.Task;

        public Task UpsertSongsAsync(IEnumerable<SyncedSong> songs, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                _upsertedSongs.AddRange(songs);
            }

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

        public Task<string?> GetDownloadUrlByFileIdAsync(string fileId, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

        public Task<JsonNode?> GetSongByIdAsync(long songId, CancellationToken cancellationToken)
            => Task.FromResult<JsonNode?>(null);

        public Task SetSyncStateAsync(string key, string value, CancellationToken cancellationToken)
        {
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
            => Task.FromResult<string?>(null);
    }
}
