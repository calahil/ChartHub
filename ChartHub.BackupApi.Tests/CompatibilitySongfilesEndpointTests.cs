using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using ChartHub.BackupApi.Persistence;
using ChartHub.BackupApi.Tests.TestInfrastructure;

namespace ChartHub.BackupApi.Tests;

[Trait(TestCategories.Category, TestCategories.IntegrationLite)]
public sealed class CompatibilitySongfilesEndpointTests : IClassFixture<BackupApiWebApplicationFactory>
{
    private readonly BackupApiWebApplicationFactory _factory;

    public CompatibilitySongfilesEndpointTests(BackupApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostSearchLiveAsync_WithText_FiltersByArtist()
    {
        await _factory.SeedSongsAsync(
        [
            Song(1, "Needle Artist", "Song A", diffGuitar: 3),
            Song(2, "Other Artist", "Song B", diffBass: 2),
        ]);

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsync(
            "/api/all/songfiles/search/live",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("text", "Needle"),
                new KeyValuePair<string, string>("page", "1"),
                new KeyValuePair<string, string>("records", "25"),
            ]));

        response.EnsureSuccessStatusCode();
        JsonElement payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        JsonElement songs = payload.GetProperty("data").GetProperty("songs");
        Assert.Equal(1, songs.GetArrayLength());
        Assert.Equal(1L, songs[0].GetProperty("data").GetProperty("song_id").GetInt64());
    }

    [Fact]
    public async Task PostListAsync_WithInstrumentFilter_UsesNonNullDiffFields()
    {
        await _factory.SeedSongsAsync(
        [
            Song(11, "Guitar Artist", "Track 11", diffGuitar: 2),
            Song(12, "No Guitar Artist", "Track 12", diffBass: 2),
        ]);

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsync(
            "/api/all/songfiles/list",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("instrument", "guitar"),
                new KeyValuePair<string, string>("page", "1"),
                new KeyValuePair<string, string>("records", "25"),
            ]));

        response.EnsureSuccessStatusCode();
        JsonElement payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement songs = payload.GetProperty("data").GetProperty("songs");

        Assert.Equal(1, songs.GetArrayLength());
        Assert.Equal(11L, songs[0].GetProperty("data").GetProperty("song_id").GetInt64());
    }

    [Fact]
    public async Task PostListAsync_WithMultipleInstrumentFilters_UsesOrSemantics()
    {
        await _factory.SeedSongsAsync(
        [
            Song(13, "Guitar Match", "Track 13", diffGuitar: 1),
            Song(14, "Bass Match", "Track 14", diffBass: 1),
            Song(15, "No Match", "Track 15"),
        ]);

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsync(
            "/api/all/songfiles/list",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("instrument", "guitar"),
                new KeyValuePair<string, string>("instrument", "bass"),
                new KeyValuePair<string, string>("sort[0][sort_by]", "song_id"),
                new KeyValuePair<string, string>("sort[0][sort_order]", "asc"),
                new KeyValuePair<string, string>("page", "1"),
                new KeyValuePair<string, string>("records", "25"),
            ]));

        response.EnsureSuccessStatusCode();
        JsonElement payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement songs = payload.GetProperty("data").GetProperty("songs");

        Assert.Equal(2, songs.GetArrayLength());
        Assert.Equal(13L, songs[0].GetProperty("data").GetProperty("song_id").GetInt64());
        Assert.Equal(14L, songs[1].GetProperty("data").GetProperty("song_id").GetInt64());
    }

    [Fact]
    public async Task PostListAsync_WithSortByArtistAsc_SortsAlphabetically()
    {
        await _factory.SeedSongsAsync(
        [
            Song(21, "Zulu Artist", "Track Z", diffGuitar: 1),
            Song(22, "Alpha Artist", "Track A", diffGuitar: 1),
        ]);

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsync(
            "/api/all/songfiles/list",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("sort[0][sort_by]", "artist"),
                new KeyValuePair<string, string>("sort[0][sort_order]", "asc"),
                new KeyValuePair<string, string>("page", "1"),
                new KeyValuePair<string, string>("records", "25"),
            ]));

        response.EnsureSuccessStatusCode();
        JsonElement payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement songs = payload.GetProperty("data").GetProperty("songs");

        Assert.Equal(2, songs.GetArrayLength());
        Assert.Equal(22L, songs[0].GetProperty("data").GetProperty("song_id").GetInt64());
        Assert.Equal(21L, songs[1].GetProperty("data").GetProperty("song_id").GetInt64());
    }

    [Fact]
    public async Task PostListAsync_WithAuthorFilter_UsesExactAuthorIdMatch()
    {
        await _factory.SeedSongsAsync(
        [
            Song(31, "Alpha", "Track 31", authorId: "author-1", recordUpdatedUnix: 10),
            Song(32, "Beta", "Track 32", authorId: "author-2", recordUpdatedUnix: 20),
        ]);

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsync(
            "/api/all/songfiles/list",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("author", "author-2"),
                new KeyValuePair<string, string>("page", "1"),
                new KeyValuePair<string, string>("records", "25"),
            ]));

        response.EnsureSuccessStatusCode();
        JsonElement payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement songs = payload.GetProperty("data").GetProperty("songs");

        Assert.Single(songs.EnumerateArray());
        Assert.Equal(32L, songs[0].GetProperty("data").GetProperty("song_id").GetInt64());
    }

    [Fact]
    public async Task PostListAsync_WithAuthorShortnameFilter_MatchesWhenAuthorIdDiffers()
    {
        await _factory.SeedSongsAsync(
        [
            new SongSnapshotEntity
            {
                SongId = 33,
                RecordId = "record-33",
                Artist = "Gamma",
                Title = "Track 33",
                Album = "Album",
                Genre = "Rock",
                FileId = "file-33",
                DownloadUrl = string.Empty,
                AuthorId = "author-id-33",
                GroupId = string.Empty,
                GameFormat = "rb3",
                SongJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    data = new { song_id = 33, artist = "Gamma", title = "Track 33" },
                    file = new { file_id = "file-33", gameformat = "rb3" },
                }),
                DataJson = "{}",
                FileJson = "{\"author\":{\"shortname\":\"shortname-33\"}}",
                IsDeleted = false,
                LastSyncedUtc = DateTimeOffset.UtcNow,
            },
            Song(34, "Delta", "Track 34", authorId: "author-id-34", recordUpdatedUnix: 20),
        ]);

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsync(
            "/api/all/songfiles/list",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("author", "SHORTNAME-33"),
                new KeyValuePair<string, string>("page", "1"),
                new KeyValuePair<string, string>("records", "25"),
            ]));

        response.EnsureSuccessStatusCode();
        JsonElement payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement songs = payload.GetProperty("data").GetProperty("songs");

        Assert.Single(songs.EnumerateArray());
        Assert.Equal(33L, songs[0].GetProperty("data").GetProperty("song_id").GetInt64());
    }

    [Fact]
    public async Task PostListAsync_WithAuthorFilterMatchingIdAndShortname_ReturnsUnionOfMatches()
    {
        await _factory.SeedSongsAsync(
        [
            new SongSnapshotEntity
            {
                SongId = 35,
                RecordId = "record-35",
                Artist = "Alpha",
                Title = "Track 35",
                Album = "Album",
                Genre = "Rock",
                FileId = "file-35",
                DownloadUrl = string.Empty,
                AuthorId = "needle",
                GroupId = string.Empty,
                GameFormat = "rb3",
                SongJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    data = new { song_id = 35, artist = "Alpha", title = "Track 35" },
                    file = new { file_id = "file-35", gameformat = "rb3" },
                }),
                DataJson = "{}",
                FileJson = "{\"author\":{\"shortname\":\"not-needle\"}}",
                IsDeleted = false,
                LastSyncedUtc = DateTimeOffset.UtcNow,
            },
            new SongSnapshotEntity
            {
                SongId = 36,
                RecordId = "record-36",
                Artist = "Beta",
                Title = "Track 36",
                Album = "Album",
                Genre = "Rock",
                FileId = "file-36",
                DownloadUrl = string.Empty,
                AuthorId = "different-id",
                GroupId = string.Empty,
                GameFormat = "rb3",
                SongJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    data = new { song_id = 36, artist = "Beta", title = "Track 36" },
                    file = new { file_id = "file-36", gameformat = "rb3" },
                }),
                DataJson = "{}",
                FileJson = "{\"author\":{\"shortname\":\"needle\"}}",
                IsDeleted = false,
                LastSyncedUtc = DateTimeOffset.UtcNow,
            },
            Song(37, "Gamma", "Track 37", authorId: "other-id", recordUpdatedUnix: 30),
        ]);

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsync(
            "/api/all/songfiles/list",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("author", "needle"),
                new KeyValuePair<string, string>("sort[0][sort_by]", "song_id"),
                new KeyValuePair<string, string>("sort[0][sort_order]", "asc"),
                new KeyValuePair<string, string>("page", "1"),
                new KeyValuePair<string, string>("records", "25"),
            ]));

        response.EnsureSuccessStatusCode();
        JsonElement payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement songs = payload.GetProperty("data").GetProperty("songs");

        Assert.Equal(2, songs.GetArrayLength());
        Assert.Equal(35L, songs[0].GetProperty("data").GetProperty("song_id").GetInt64());
        Assert.Equal(36L, songs[1].GetProperty("data").GetProperty("song_id").GetInt64());
    }

    [Fact]
    public async Task PostListAsync_WithUnknownSortKey_FallsBackToDefaultOrder()
    {
        await _factory.SeedSongsAsync(
        [
            Song(41, "Older", "Track 41", recordUpdatedUnix: 100),
            Song(42, "Newer", "Track 42", recordUpdatedUnix: 200),
        ]);

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsync(
            "/api/all/songfiles/list",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("sort[0][sort_by]", "not_a_real_sort"),
                new KeyValuePair<string, string>("sort[0][sort_order]", "asc"),
                new KeyValuePair<string, string>("page", "1"),
                new KeyValuePair<string, string>("records", "25"),
            ]));

        response.EnsureSuccessStatusCode();
        JsonElement payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement songs = payload.GetProperty("data").GetProperty("songs");

        Assert.Equal(2, songs.GetArrayLength());
        Assert.Equal(42L, songs[0].GetProperty("data").GetProperty("song_id").GetInt64());
        Assert.Equal(41L, songs[1].GetProperty("data").GetProperty("song_id").GetInt64());
    }

    [Fact]
    public async Task PostSearchLiveAsync_WithEmptyText_BehavesLikeList()
    {
        await _factory.SeedSongsAsync(
        [
            Song(51, "Alpha", "Track 51", recordUpdatedUnix: 100),
            Song(52, "Beta", "Track 52", recordUpdatedUnix: 200),
        ]);

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsync(
            "/api/all/songfiles/search/live",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("text", "   "),
                new KeyValuePair<string, string>("page", "1"),
                new KeyValuePair<string, string>("records", "25"),
            ]));

        response.EnsureSuccessStatusCode();
        JsonElement payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement songs = payload.GetProperty("data").GetProperty("songs");

        Assert.Equal(2, songs.GetArrayLength());
    }

    [Fact]
    public async Task PostListAsync_WithUnknownInstrument_IgnoresInstrumentFilter()
    {
        await _factory.SeedSongsAsync(
        [
            Song(61, "Alpha", "Track 61", diffGuitar: 1),
            Song(62, "Beta", "Track 62"),
        ]);

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsync(
            "/api/all/songfiles/list",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("instrument", "not-a-real-instrument"),
                new KeyValuePair<string, string>("page", "1"),
                new KeyValuePair<string, string>("records", "25"),
            ]));

        response.EnsureSuccessStatusCode();
        JsonElement payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement songs = payload.GetProperty("data").GetProperty("songs");

        Assert.Equal(2, songs.GetArrayLength());
    }

    [Fact]
    public async Task PostListAsync_WithMalformedPagination_UsesDefaults()
    {
        await _factory.SeedSongsAsync(
        [
            Song(71, "A", "Track 71", recordUpdatedUnix: 300),
            Song(72, "B", "Track 72", recordUpdatedUnix: 200),
            Song(73, "C", "Track 73", recordUpdatedUnix: 100),
        ]);

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsync(
            "/api/all/songfiles/list",
            new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("page", "oops"),
                new KeyValuePair<string, string>("records", "nope"),
            ]));

        response.EnsureSuccessStatusCode();
        JsonElement payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement pagination = payload.GetProperty("data").GetProperty("pagination");
        JsonElement songs = payload.GetProperty("data").GetProperty("songs");

        Assert.Equal(1, pagination.GetProperty("page").GetInt32());
        Assert.Equal(25, pagination.GetProperty("records").GetInt32());
        Assert.Equal(3, songs.GetArrayLength());
    }

    [Fact]
    public async Task LegacyGetSongsRoute_IsNotMapped()
    {
        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/api/rhythmverse/songs");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static SongSnapshotEntity Song(
        long id,
        string artist,
        string title,
        int? diffGuitar = null,
        int? diffBass = null,
        string authorId = "",
        long? recordUpdatedUnix = null) =>
        new()
        {
            SongId = id,
            RecordId = $"record-{id}",
            Artist = artist,
            Title = title,
            Album = "Album",
            Genre = "Rock",
            FileId = $"file-{id}",
            DownloadUrl = string.Empty,
            AuthorId = authorId,
            GroupId = string.Empty,
            GameFormat = "rb3",
            RecordUpdatedUnix = recordUpdatedUnix,
            DiffGuitar = diffGuitar,
            DiffBass = diffBass,
            SongJson = System.Text.Json.JsonSerializer.Serialize(
                new
                {
                    data = new
                    {
                        song_id = id,
                        artist,
                        title,
                        diff_guitar = diffGuitar,
                        diff_bass = diffBass,
                    },
                    file = new
                    {
                        file_id = $"file-{id}",
                        gameformat = "rb3",
                    },
                }),
            DataJson = "{}",
            FileJson = "{}",
            IsDeleted = false,
            LastSyncedUtc = DateTimeOffset.UtcNow,
        };
}
