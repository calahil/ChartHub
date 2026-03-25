using System.Text.Json.Nodes;

using ChartHub.BackupApi.Models;
using ChartHub.BackupApi.Services;
using ChartHub.BackupApi.Tests.TestInfrastructure;

namespace ChartHub.BackupApi.Tests;

[Trait(TestCategories.Category, TestCategories.IntegrationLite)]
public class RepositoryTests : IDisposable
{
    private readonly SqliteTestContext _testContext;
    private readonly RhythmVerseRepository _sut;

    public RepositoryTests()
    {
        _testContext = new SqliteTestContext();
        _sut = new RhythmVerseRepository(_testContext.DbContext);
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
        long? recordUpdatedUnix = null) =>
        new()
        {
            SongId = id,
            RecordId = $"record-{id}",
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
            SongJson = BuildSongJson(id, artist, title, album, genre),
            DataJson = BuildDataJson(id, artist, title, album, genre),
            FileJson = "{}",
        };

    private static string BuildSongJson(long id, string artist, string title, string album, string genre)
        => System.Text.Json.JsonSerializer.Serialize(
            new { data = new { song_id = id, artist, title, album, genre }, file = new { } });

    private static string BuildDataJson(long id, string artist, string title, string album, string genre)
        => System.Text.Json.JsonSerializer.Serialize(
            new { song_id = id, artist, title, album, genre });
}
