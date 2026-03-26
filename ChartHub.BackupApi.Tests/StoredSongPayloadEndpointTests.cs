using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using ChartHub.BackupApi.Persistence;
using ChartHub.BackupApi.Tests.TestInfrastructure;

namespace ChartHub.BackupApi.Tests;

[Trait(TestCategories.Category, TestCategories.IntegrationLite)]
public sealed class StoredSongPayloadEndpointTests : IClassFixture<BackupApiWebApplicationFactory>
{
    private readonly BackupApiWebApplicationFactory _factory;

    public StoredSongPayloadEndpointTests(BackupApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetSongByIdAsync_WithMalformedStoredJson_Returns500ProblemDetails()
    {
        await _factory.SeedSongsAsync([MalformedSong(501)]);

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/api/rhythmverse/songs/501");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Stored RhythmVerse payload is invalid", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task PostListAsync_WithMalformedStoredJson_Returns500ProblemDetails()
    {
        await _factory.SeedSongsAsync([MalformedSong(502)]);

        HttpClient client = _factory.CreateClient();
        HttpResponseMessage response = await client.PostAsync(
            "/api/all/songfiles/list",
            new FormUrlEncodedContent([
                new KeyValuePair<string, string>("page", "1"),
                new KeyValuePair<string, string>("records", "25"),
            ]));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Stored RhythmVerse payload is invalid", problem.GetProperty("title").GetString());
    }

    private static SongSnapshotEntity MalformedSong(long id) =>
        new()
        {
            SongId = id,
            RecordId = $"record-{id}",
            Artist = "Broken Artist",
            Title = "Broken Title",
            Album = "Broken Album",
            Genre = "Rock",
            FileId = string.Empty,
            DownloadUrl = string.Empty,
            AuthorId = string.Empty,
            GroupId = string.Empty,
            GameFormat = string.Empty,
            SongJson = "{",
            DataJson = "{}",
            FileJson = "{}",
            IsDeleted = false,
            LastSyncedUtc = DateTimeOffset.UtcNow,
        };
}
