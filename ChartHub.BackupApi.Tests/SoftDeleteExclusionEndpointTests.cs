using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

using ChartHub.BackupApi.Persistence;
using ChartHub.BackupApi.Tests.TestInfrastructure;

namespace ChartHub.BackupApi.Tests;

[Trait(TestCategories.Category, TestCategories.IntegrationLite)]
public sealed class SoftDeleteExclusionEndpointTests : IClassFixture<BackupApiWebApplicationFactory>
{
    private readonly BackupApiWebApplicationFactory _factory;

    public SoftDeleteExclusionEndpointTests(BackupApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostListAsync_ExcludesSoftDeletedSongs()
    {
        await _factory.SeedSongsAsync(
        [
            Song(1, "Live Artist", isDeleted: false),
            Song(2, "Deleted Artist", isDeleted: true),
        ]);

        HttpClient client = _factory.CreateAuthenticatedClient();
        HttpResponseMessage httpResponse = await client.PostAsync(
            "/api/all/songfiles/list",
            new FormUrlEncodedContent([
                new KeyValuePair<string, string>("page", "1"),
                new KeyValuePair<string, string>("records", "25"),
            ]));
        JsonElement response = await httpResponse.Content.ReadFromJsonAsync<JsonElement>();

        JsonElement records = response.GetProperty("data").GetProperty("records");
        JsonElement songs = response.GetProperty("data").GetProperty("songs");

        Assert.Equal(1, records.GetProperty("total_available").GetInt32());
        Assert.Equal(1, records.GetProperty("returned").GetInt32());
        Assert.Equal(1, songs.GetArrayLength());

        long returnedId = songs[0].GetProperty("data").GetProperty("song_id").GetInt64();
        Assert.Equal(1L, returnedId);
    }

    [Fact]
    public async Task GetSongByIdAsync_WithSoftDeletedSong_Returns404()
    {
        await _factory.SeedSongsAsync(
        [
            Song(99, "Deleted Artist", isDeleted: true),
        ]);

        HttpClient client = _factory.CreateAuthenticatedClient();
        HttpResponseMessage response = await client.GetAsync("/api/rhythmverse/songs/99");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static SongSnapshotEntity Song(long id, string artist, bool isDeleted = false) =>
        new()
        {
            SongId = id,
            RecordId = $"record-{id}",
            Artist = artist,
            Title = "Test Title",
            Album = "Test Album",
            Genre = "Rock",
            FileId = string.Empty,
            DownloadUrl = string.Empty,
            AuthorId = string.Empty,
            GroupId = string.Empty,
            GameFormat = string.Empty,
            SongJson = System.Text.Json.JsonSerializer.Serialize(
                new { data = new { song_id = id, artist }, file = new { } }),
            DataJson = System.Text.Json.JsonSerializer.Serialize(
                new { song_id = id, artist }),
            FileJson = "{}",
            IsDeleted = isDeleted,
            LastSyncedUtc = DateTimeOffset.UtcNow,
        };
}
