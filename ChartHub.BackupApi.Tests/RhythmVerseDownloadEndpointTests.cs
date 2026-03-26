using ChartHub.BackupApi.Persistence;
using ChartHub.BackupApi.Tests.TestInfrastructure;

using Microsoft.AspNetCore.Mvc.Testing;

namespace ChartHub.BackupApi.Tests;

[Trait(TestCategories.Category, TestCategories.IntegrationLite)]
public sealed class RhythmVerseDownloadEndpointTests : IClassFixture<BackupApiWebApplicationFactory>
{
    private readonly BackupApiWebApplicationFactory _factory;

    public RhythmVerseDownloadEndpointTests(BackupApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HeadDownloadRedirect_WhenFileExists_ReturnsFoundAndLocation()
    {
        await _factory.SeedSongsAsync(
        [
            new SongSnapshotEntity
            {
                SongId = 101,
                RecordId = "record-101",
                Artist = "Test Artist",
                Title = "Test Title",
                Album = "Test Album",
                Genre = "Rock",
                FileId = "abc123",
                DownloadUrl = "https://example.com/download/abc123",
                SongJson = "{}",
                DataJson = "{}",
                FileJson = "{}",
                IsDeleted = false,
                LastSyncedUtc = DateTimeOffset.UtcNow,
            },
        ]);

        using HttpClient client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        using HttpRequestMessage request = new(HttpMethod.Head, "/api/rhythmverse/download/abc123");
        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("https://example.com/download/abc123", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task HeadDownloadRedirect_WhenFileDoesNotExist_Returns404()
    {
        await _factory.SeedSongsAsync([]);

        using HttpClient client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        using HttpRequestMessage request = new(HttpMethod.Head, "/api/rhythmverse/download/missing-file");
        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }
}
