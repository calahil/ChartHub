using System.Text.Json.Nodes;

using ChartHub.BackupApi.Models;
using ChartHub.BackupApi.Persistence;
using ChartHub.BackupApi.Services;
using ChartHub.BackupApi.Tests.TestInfrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChartHub.BackupApi.Tests;

[Trait(TestCategories.Category, TestCategories.IntegrationLite)]
public class RepositoryTests : IDisposable
{
    private readonly SqliteTestContext _testContext;
    private readonly RhythmVerseRepository _sut;

    public RepositoryTests()
    {
        _testContext = new SqliteTestContext();
        _sut = new RhythmVerseRepository(_testContext.DbContext, NullLogger<RhythmVerseRepository>.Instance);
    }

    public void Dispose() => _testContext.Dispose();

    [Fact]
    public async Task UpsertSongsAsync_ThenGetPage_ReturnsAllUpsertedSongs()
    {
        await _sut.UpsertSongsAsync(
            [Song(1, "Artist A", genre: "Rock"), Song(2, "Artist B", genre: "Metal")],
            CancellationToken.None);

        RhythmVersePageEnvelope envelope = await _sut.GetSongsPageAsync(
            1, 10, null, null, null, null, null, CancellationToken.None);

        Assert.Equal(2, envelope.Returned);
        Assert.Equal(2, envelope.TotalAvailable);
    }

    [Fact]
    public async Task UpsertSongsAsync_IsIdempotent_UpdatesExistingRecord()
    {
        await _sut.UpsertSongsAsync([Song(1, "Original Artist")], CancellationToken.None);
        await _sut.UpsertSongsAsync([Song(1, "Updated Artist")], CancellationToken.None);

        RhythmVersePageEnvelope envelope = await _sut.GetSongsPageAsync(
            1, 10, null, null, null, null, null, CancellationToken.None);

        Assert.Equal(1, envelope.Returned);

        JsonNode? first = envelope.Songs[0];
        Assert.NotNull(first);
        Assert.Equal("Updated Artist", (string?)first["data"]?["artist"]);
    }

    [Fact]
    public async Task UpsertSongsAsync_WithMatchingRecordIdAndSongIdAcrossCalls_UpdatesExistingRow()
    {
        await _sut.UpsertSongsAsync([Song(1, "Original Artist", recordId: "shared-record")], CancellationToken.None);
        await _sut.UpsertSongsAsync([Song(1, "Updated Artist", recordId: "shared-record")], CancellationToken.None);

        SongSnapshotEntity entity = await _testContext.DbContext.SongSnapshots
            .SingleAsync(x => x.RecordId == "shared-record", CancellationToken.None);

        Assert.Equal(1L, entity.SongId);
        Assert.Equal("Updated Artist", entity.Artist);
    }

    [Fact]
    public async Task UpsertSongsAsync_WithConflictingRecordIdAcrossCalls_PreservesExistingRow()
    {
        await _sut.UpsertSongsAsync([Song(1, "Original Artist", recordId: "shared-record")], CancellationToken.None);

        await _sut.UpsertSongsAsync([Song(2, "Conflicting Artist", recordId: "shared-record")], CancellationToken.None);

        List<SongSnapshotEntity> entities = await _testContext.DbContext.SongSnapshots
            .OrderBy(x => x.SongId)
            .ToListAsync(CancellationToken.None);

        Assert.Single(entities);
        Assert.Equal(1L, entities[0].SongId);
        Assert.Equal("Original Artist", entities[0].Artist);
        Assert.Null(await _sut.GetSongByIdAsync(2L, CancellationToken.None));
    }

    [Fact]
    public async Task UpsertSongsAsync_WithConflictingRecordIdInBatch_KeepsFirstRow()
    {
        await _sut.UpsertSongsAsync(
            [
                Song(1, "Original Artist", recordId: "shared-record"),
                Song(2, "Conflicting Artist", recordId: "shared-record"),
            ],
            CancellationToken.None);

        List<SongSnapshotEntity> entities = await _testContext.DbContext.SongSnapshots
            .OrderBy(x => x.SongId)
            .ToListAsync(CancellationToken.None);

        Assert.Single(entities);
        Assert.Equal(1L, entities[0].SongId);
        Assert.Equal("Original Artist", entities[0].Artist);
    }

    [Fact]
    public async Task UpsertSongsAsync_WithNullRecordIdOnMultipleSongs_InsertsAllRowsWithoutCollision()
    {
        SyncedSong song1 = new()
        {
            SongId = 1,
            RecordId = null,
            Artist = "Artist A",
            SongJson = "{}",
            DataJson = "{}",
            FileJson = "{}",
        };
        SyncedSong song2 = new()
        {
            SongId = 2,
            RecordId = null,
            Artist = "Artist B",
            SongJson = "{}",
            DataJson = "{}",
            FileJson = "{}",
        };

        await _sut.UpsertSongsAsync([song1], CancellationToken.None);
        await _sut.UpsertSongsAsync([song2], CancellationToken.None);

        RhythmVersePageEnvelope result = await _sut.GetSongsPageAsync(
            1, 10, null, null, null, null, null, CancellationToken.None);

        Assert.Equal(2, result.Returned);
        Assert.Equal(2, result.TotalAvailable);
    }

    [Fact]
    public async Task UpsertSongsAsync_WithDuplicateSongIdInBatch_KeepsOneRecordWithLaterValues()
    {
        await _sut.UpsertSongsAsync(
            [
                Song(1, "First Artist", title: "First Title"),
                Song(1, "Second Artist", title: "Second Title"),
            ],
            CancellationToken.None);

        RhythmVersePageEnvelope envelope = await _sut.GetSongsPageAsync(
            1, 10, null, null, null, null, null, CancellationToken.None);

        Assert.Equal(1, envelope.Returned);
        Assert.Equal(1, envelope.TotalAvailable);
        Assert.Equal("Second Artist", (string?)envelope.Songs[0]?["data"]?["artist"]);
        Assert.Equal("Second Title", (string?)envelope.Songs[0]?["data"]?["title"]);
    }

    [Fact]
    public async Task FinalizeReconciliationRunAsync_SoftDeletesSongsNotSeenInCompletedRun()
    {
        await _sut.UpsertSongsAsync([Song(1, "Artist A"), Song(2, "Artist B")], CancellationToken.None, "run-1");
        await _sut.FinalizeReconciliationRunAsync("run-1", CancellationToken.None);

        await _sut.UpsertSongsAsync([Song(1, "Artist A Updated")], CancellationToken.None, "run-2");
        await _sut.FinalizeReconciliationRunAsync("run-2", CancellationToken.None);

        RhythmVersePageEnvelope envelope = await _sut.GetSongsPageAsync(
            1, 10, null, null, null, null, null, CancellationToken.None);

        Assert.Equal(1, envelope.Returned);
        Assert.Equal(1, envelope.TotalAvailable);
        Assert.Equal(1L, (long?)envelope.Songs[0]?["data"]?["song_id"]);
        Assert.Null(await _sut.GetSongByIdAsync(2L, CancellationToken.None));

        SongSnapshotEntity deletedSong = await _testContext.DbContext.SongSnapshots
            .SingleAsync(x => x.SongId == 2L, CancellationToken.None);

        Assert.True(deletedSong.IsDeleted);
    }

    [Fact]
    public async Task UpsertSongsAsync_WithReappearingSong_RestoresSoftDeletedRow()
    {
        await _sut.UpsertSongsAsync(
            [Song(1, "Artist A", recordId: "record-one"), Song(2, "Artist B", recordId: "record-two")],
            CancellationToken.None,
            "run-1");
        await _sut.FinalizeReconciliationRunAsync("run-1", CancellationToken.None);

        await _sut.UpsertSongsAsync([Song(1, "Artist A", recordId: "record-one")], CancellationToken.None, "run-2");
        await _sut.FinalizeReconciliationRunAsync("run-2", CancellationToken.None);

        await _sut.UpsertSongsAsync(
            [Song(1, "Artist A", recordId: "record-one"), Song(2, "Artist B Restored", recordId: "record-two")],
            CancellationToken.None,
            "run-3");
        await _sut.FinalizeReconciliationRunAsync("run-3", CancellationToken.None);

        JsonNode? restoredSong = await _sut.GetSongByIdAsync(2L, CancellationToken.None);
        SongSnapshotEntity entity = await _testContext.DbContext.SongSnapshots
            .SingleAsync(x => x.SongId == 2L, CancellationToken.None);

        Assert.NotNull(restoredSong);
        Assert.Equal("Artist B Restored", (string?)restoredSong["data"]?["artist"]);
        Assert.False(entity.IsDeleted);
        Assert.Equal("run-3", entity.LastReconciledRunId);
    }

    [Fact]
    public async Task GetSongsPageAsync_WithGenreFilter_ReturnsOnlyMatchingGenre()
    {
        await _sut.UpsertSongsAsync(
            [
                Song(1, "A", genre: "Rock"),
                Song(2, "B", genre: "Metal"),
                Song(3, "C", genre: "Metal"),
            ],
            CancellationToken.None);

        RhythmVersePageEnvelope envelope = await _sut.GetSongsPageAsync(
            1, 10, null, "Metal", null, null, null, CancellationToken.None);

        Assert.Equal(2, envelope.Returned);
        Assert.Equal(2, envelope.TotalFiltered);
        Assert.Equal(3, envelope.TotalAvailable);
    }

    [Fact]
    public async Task GetSongsPageAsync_WithAuthorFilter_ReturnsOnlyMatchingAuthor()
    {
        await _sut.UpsertSongsAsync(
            [Song(1, "A", authorId: "alice"), Song(2, "B", authorId: "bob")],
            CancellationToken.None);

        RhythmVersePageEnvelope envelope = await _sut.GetSongsPageAsync(
            1, 10, null, null, null, "alice", null, CancellationToken.None);

        Assert.Equal(1, envelope.Returned);
    }

    [Fact]
    public async Task GetSongsPageAsync_WithGroupFilter_ReturnsOnlyMatchingGroup()
    {
        await _sut.UpsertSongsAsync(
            [Song(1, "A", groupId: "c3"), Song(2, "B", groupId: "gh")],
            CancellationToken.None);

        RhythmVersePageEnvelope envelope = await _sut.GetSongsPageAsync(
            1, 10, null, null, null, null, "c3", CancellationToken.None);

        Assert.Equal(1, envelope.Returned);
    }

    [Fact]
    public async Task GetSongsPageAsync_WithGameFormatFilter_ReturnsOnlyMatchingFormat()
    {
        await _sut.UpsertSongsAsync(
            [Song(1, "A", gameformat: "rb3xbox"), Song(2, "B", gameformat: "ps3")],
            CancellationToken.None);

        RhythmVersePageEnvelope envelope = await _sut.GetSongsPageAsync(
            1, 10, null, null, "rb3xbox", null, null, CancellationToken.None);

        Assert.Equal(1, envelope.Returned);
    }

    [Fact]
    public async Task GetSongsPageAsync_WithTextQuery_MatchesArtistField()
    {
        await _sut.UpsertSongsAsync(
            [
                Song(1, "Guns N' Roses", title: "Sweet Child"),
                Song(2, "Eagles", title: "Hotel California"),
            ],
            CancellationToken.None);

        RhythmVersePageEnvelope envelope = await _sut.GetSongsPageAsync(
            1, 10, "Eagles", null, null, null, null, CancellationToken.None);

        Assert.Equal(1, envelope.Returned);
    }

    [Fact]
    public async Task GetSongsPageAsync_WithTextQuery_MatchesPartialArtistName()
    {
        // "Weird" is a partial match for "Weird Al Yankovic".
        await _sut.UpsertSongsAsync(
            [
                Song(1, "Weird Al Yankovic", title: "Eat It"),
                Song(2, "Another Artist", title: "Another Song"),
            ],
            CancellationToken.None);

        RhythmVersePageEnvelope envelope = await _sut.GetSongsPageAsync(
            1, 10, "Weird", null, null, null, null, CancellationToken.None);

        Assert.Equal(1, envelope.Returned);
        Assert.Equal(1L, (long?)envelope.Songs[0]?["data"]?["song_id"]);
    }

    [Fact]
    public async Task GetSongsPageAsync_WithPunctuationInQuery_MatchesArtistContainingPunctuation()
    {
        // Query "AC/DC" must match the stored artist "AC/DC".
        await _sut.UpsertSongsAsync(
            [
                Song(1, "AC/DC", title: "Highway to Hell"),
                Song(2, "Another Band", title: "Other Song"),
            ],
            CancellationToken.None);

        RhythmVersePageEnvelope envelope = await _sut.GetSongsPageAsync(
            1, 10, "AC/DC", null, null, null, null, CancellationToken.None);

        Assert.Equal(1, envelope.Returned);
        Assert.Equal(1L, (long?)envelope.Songs[0]?["data"]?["song_id"]);
    }

    [Fact]
    public async Task GetSongsPageAsync_WithQueryLackingPunctuation_MatchesArtistThatHasPunctuation()
    {
        // Query "ACDC" (no slash) must still find the artist stored as "AC/DC"
        // because the search normalises both sides by stripping connector punctuation.
        await _sut.UpsertSongsAsync(
            [
                Song(1, "AC/DC", title: "Back in Black"),
                Song(2, "Another Band", title: "Another Song"),
            ],
            CancellationToken.None);

        RhythmVersePageEnvelope envelope = await _sut.GetSongsPageAsync(
            1, 10, "ACDC", null, null, null, null, CancellationToken.None);

        Assert.Equal(1, envelope.Returned);
        Assert.Equal(1L, (long?)envelope.Songs[0]?["data"]?["song_id"]);
    }

    [Fact]
    public async Task GetSongsPageAsync_WithLowercaseQuery_MatchesMixedCaseArtist()
    {
        // Case-insensitive: "weird" must match "Weird Al Yankovic".
        await _sut.UpsertSongsAsync(
            [
                Song(1, "Weird Al Yankovic", title: "White and Nerdy"),
                Song(2, "Normal Artist", title: "Normal Song"),
            ],
            CancellationToken.None);

        RhythmVersePageEnvelope envelope = await _sut.GetSongsPageAsync(
            1, 10, "weird", null, null, null, null, CancellationToken.None);

        Assert.Equal(1, envelope.Returned);
        Assert.Equal(1L, (long?)envelope.Songs[0]?["data"]?["song_id"]);
    }

    [Fact]
    public async Task GetSongsPageAsync_Pagination_ReturnsCorrectSlices()
    {
        IEnumerable<SyncedSong> songs = Enumerable.Range(1, 5)
            .Select(i => Song(i, $"Artist {i}", recordUpdatedUnix: (long)i));

        await _sut.UpsertSongsAsync(songs, CancellationToken.None);

        RhythmVersePageEnvelope page1 = await _sut.GetSongsPageAsync(
            1, 2, null, null, null, null, null, CancellationToken.None);
        RhythmVersePageEnvelope page2 = await _sut.GetSongsPageAsync(
            2, 2, null, null, null, null, null, CancellationToken.None);

        Assert.Equal(2, page1.Returned);
        Assert.Equal(2, page2.Returned);
        Assert.Equal(5, page1.TotalAvailable);
        Assert.Equal(0, page1.Start);
        Assert.Equal(2, page2.Start);
    }

    [Fact]
    public async Task GetSongsPageAdvancedAsync_WithInstrumentFilter_UsesNonNullDifficultyFields()
    {
        await _sut.UpsertSongsAsync(
            [
                Song(1, "A", title: "With Guitar", diffGuitar: 2),
                Song(2, "B", title: "Without Guitar", diffBass: 3),
            ],
            CancellationToken.None);

        RhythmVersePageEnvelope envelope = await _sut.GetSongsPageAdvancedAsync(
            page: 1,
            records: 25,
            query: null,
            genre: null,
            gameformat: null,
            author: null,
            group: null,
            sortBy: null,
            sortOrder: null,
            instruments: ["guitar"],
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, envelope.Returned);
        Assert.Equal(1L, (long?)envelope.Songs[0]?["data"]?["song_id"]);
    }

    [Fact]
    public async Task GetSongsPageAdvancedAsync_WithArtistAscSort_ReturnsAlphabeticalOrder()
    {
        await _sut.UpsertSongsAsync(
            [
                Song(10, "Zulu Artist", title: "Z"),
                Song(11, "Alpha Artist", title: "A"),
            ],
            CancellationToken.None);

        RhythmVersePageEnvelope envelope = await _sut.GetSongsPageAdvancedAsync(
            page: 1,
            records: 25,
            query: null,
            genre: null,
            gameformat: null,
            author: null,
            group: null,
            sortBy: "artist",
            sortOrder: "asc",
            instruments: null,
            cancellationToken: CancellationToken.None);

        Assert.Equal(2, envelope.Returned);
        Assert.Equal(11L, (long?)envelope.Songs[0]?["data"]?["song_id"]);
        Assert.Equal(10L, (long?)envelope.Songs[1]?["data"]?["song_id"]);
    }

    [Fact]
    public async Task GetSongsPageAdvancedAsync_WithLengthSortAlias_UsesRequestedSortDirection()
    {
        await _sut.UpsertSongsAsync(
            [
                Song(20, "A", title: "Older", recordUpdatedUnix: 100),
                Song(21, "B", title: "Newer", recordUpdatedUnix: 200),
            ],
            CancellationToken.None);

        RhythmVersePageEnvelope envelope = await _sut.GetSongsPageAdvancedAsync(
            page: 1,
            records: 25,
            query: null,
            genre: null,
            gameformat: null,
            author: null,
            group: null,
            sortBy: "length",
            sortOrder: "asc",
            instruments: null,
            cancellationToken: CancellationToken.None);

        Assert.Equal(2, envelope.Returned);
        Assert.Equal(20L, (long?)envelope.Songs[0]?["data"]?["song_id"]);
        Assert.Equal(21L, (long?)envelope.Songs[1]?["data"]?["song_id"]);
    }

    [Fact]
    public async Task GetDownloadUrlByFileIdAsync_ReturnsStoredUrl()
    {
        await _sut.UpsertSongsAsync(
            [Song(1, "A", fileId: "abc123", downloadUrl: "https://example.com/dl/abc123")],
            CancellationToken.None);

        string? url = await _sut.GetDownloadUrlByFileIdAsync("abc123", CancellationToken.None);

        Assert.Equal("https://example.com/dl/abc123", url);
    }

    [Fact]
    public async Task GetDownloadUrlByFileIdAsync_WhenNotFound_ReturnsNull()
    {
        string? url = await _sut.GetDownloadUrlByFileIdAsync("nope", CancellationToken.None);

        Assert.Null(url);
    }

    [Fact]
    public async Task GetDownloadUrlByFileIdAsync_WithSoftDeletedSong_ReturnsNull()
    {
        await _sut.UpsertSongsAsync(
            [Song(1, "A", fileId: "abc123", downloadUrl: "https://example.com/dl/abc123")],
            CancellationToken.None,
            "run-1");
        await _sut.FinalizeReconciliationRunAsync("run-1", CancellationToken.None);

        await _sut.FinalizeReconciliationRunAsync("run-2", CancellationToken.None);

        string? url = await _sut.GetDownloadUrlByFileIdAsync("abc123", CancellationToken.None);

        Assert.Null(url);
    }

    [Fact]
    public async Task GetSongByIdAsync_ReturnsJsonNodeWithCorrectSongId()
    {
        await _sut.UpsertSongsAsync([Song(42, "A")], CancellationToken.None);

        JsonNode? node = await _sut.GetSongByIdAsync(42L, CancellationToken.None);

        Assert.NotNull(node);
        Assert.Equal(42L, (long?)node["data"]?["song_id"]);
    }

    [Fact]
    public async Task GetSongByIdAsync_WhenNotFound_ReturnsNull()
    {
        JsonNode? node = await _sut.GetSongByIdAsync(999L, CancellationToken.None);

        Assert.Null(node);
    }

    [Fact]
    public async Task GetSongByIdAsync_WithMalformedStoredSongJson_ThrowsStoredSongPayloadException()
    {
        await SeedMalformedSongAsync(77L);

        StoredSongPayloadException exception = await Assert.ThrowsAsync<StoredSongPayloadException>(() =>
            _sut.GetSongByIdAsync(77L, CancellationToken.None));

        Assert.Contains("77", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSongsPageAsync_WithMalformedStoredSongJson_ThrowsStoredSongPayloadException()
    {
        await SeedMalformedSongAsync(88L);

        StoredSongPayloadException exception = await Assert.ThrowsAsync<StoredSongPayloadException>(() =>
            _sut.GetSongsPageAsync(1, 25, null, null, null, null, null, CancellationToken.None));

        Assert.Contains("88", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetAndGetSyncState_RoundTrips()
    {
        await _sut.SetSyncStateAsync("test.key", "hello-world", CancellationToken.None);
        string? value = await _sut.GetSyncStateAsync("test.key", CancellationToken.None);

        Assert.Equal("hello-world", value);
    }

    [Fact]
    public async Task SetSyncStateAsync_IsIdempotent_OverwritesValue()
    {
        await _sut.SetSyncStateAsync("test.key", "value1", CancellationToken.None);
        await _sut.SetSyncStateAsync("test.key", "value2", CancellationToken.None);
        string? value = await _sut.GetSyncStateAsync("test.key", CancellationToken.None);

        Assert.Equal("value2", value);
    }

    private static SyncedSong Song(
        long id,
        string artist,
        string title = "Test Title",
        string album = "Test Album",
        string genre = "Rock",
        string fileId = "",
        string downloadUrl = "",
        string authorId = "",
        string groupId = "",
        string gameformat = "",
        string? recordId = null,
        long? recordUpdatedUnix = null,
        int? diffGuitar = null,
        int? diffBass = null,
        int? diffDrums = null,
        int? diffVocals = null,
        int? diffKeys = null,
        int? diffBand = null) =>
        new()
        {
            SongId = id,
            RecordId = recordId ?? $"record-{id}",
            Artist = artist,
            Title = title,
            Album = album,
            Genre = genre,
            FileId = fileId,
            DownloadUrl = downloadUrl,
            AuthorId = authorId,
            GroupId = groupId,
            GameFormat = gameformat,
            RecordUpdatedUnix = recordUpdatedUnix,
            DiffGuitar = diffGuitar,
            DiffBass = diffBass,
            DiffDrums = diffDrums,
            DiffVocals = diffVocals,
            DiffKeys = diffKeys,
            DiffBand = diffBand,
            SongJson = BuildSongJson(id, artist, title, album, genre, diffGuitar, diffBass, diffDrums, diffVocals, diffKeys, diffBand),
            DataJson = BuildDataJson(id, artist, title, album, genre, diffGuitar, diffBass, diffDrums, diffVocals, diffKeys, diffBand),
            FileJson = "{}",
        };

    private static string BuildSongJson(
        long id,
        string artist,
        string title,
        string album,
        string genre,
        int? diffGuitar,
        int? diffBass,
        int? diffDrums,
        int? diffVocals,
        int? diffKeys,
        int? diffBand)
        => System.Text.Json.JsonSerializer.Serialize(
            new
            {
                data = new
                {
                    song_id = id,
                    artist,
                    title,
                    album,
                    genre,
                    diff_guitar = diffGuitar,
                    diff_bass = diffBass,
                    diff_drums = diffDrums,
                    diff_vocals = diffVocals,
                    diff_keys = diffKeys,
                    diff_band = diffBand,
                },
                file = new { },
            });

    private static string BuildDataJson(
        long id,
        string artist,
        string title,
        string album,
        string genre,
        int? diffGuitar,
        int? diffBass,
        int? diffDrums,
        int? diffVocals,
        int? diffKeys,
        int? diffBand)
        => System.Text.Json.JsonSerializer.Serialize(
            new
            {
                song_id = id,
                artist,
                title,
                album,
                genre,
                diff_guitar = diffGuitar,
                diff_bass = diffBass,
                diff_drums = diffDrums,
                diff_vocals = diffVocals,
                diff_keys = diffKeys,
                diff_band = diffBand,
            });

    private async Task SeedMalformedSongAsync(long songId)
    {
        _testContext.DbContext.SongSnapshots.Add(new SongSnapshotEntity
        {
            SongId = songId,
            RecordId = $"record-{songId}",
            Artist = "Broken Artist",
            Title = "Broken Title",
            Album = "Broken Album",
            Genre = "Broken Genre",
            SongJson = "{",
            DataJson = "{}",
            FileJson = "{}",
            LastSyncedUtc = DateTimeOffset.UtcNow,
        });

        await _testContext.DbContext.SaveChangesAsync(CancellationToken.None);
    }
}
