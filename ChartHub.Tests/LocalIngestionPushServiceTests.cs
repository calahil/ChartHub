using ChartHub.Models;
using ChartHub.Services;
using ChartHub.Tests.TestInfrastructure;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public sealed class LocalIngestionPushServiceTests
{
    [Fact]
    public async Task PushAsync_WithMissingPath_Throws()
    {
        var service = new LocalIngestionPushService(new FakeDesktopSyncApiClient());

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PushAsync("http://127.0.0.1:15123", "token", new LocalIngestionEntry()));

        Assert.Equal("Local file path is required.", ex.Message);
    }

    [Fact]
    public async Task PushAsync_WithExistingFile_ForwardsMetadataAndReturnsIngestionId()
    {
        using var temp = new TemporaryDirectoryFixture("local-ingestion-push");
        string localFilePath = Path.Combine(temp.RootPath, "song-one.zip");
        await File.WriteAllBytesAsync(localFilePath, [1, 2, 3, 4]);

        var fakeClient = new FakeDesktopSyncApiClient();
        var service = new LocalIngestionPushService(fakeClient);

        long ingestionId = await service.PushAsync(
            "http://127.0.0.1:15123",
            "token-123",
            new LocalIngestionEntry
            {
                LocalPath = localFilePath,
                DisplayName = "Song One.zip",
                Source = "encore",
                SourceId = "encore-id-7",
                Artist = "Artist",
                Title = "Title",
                Charter = "Charter",
                LibrarySource = "rhythmverse",
            });

        Assert.Equal(44L, ingestionId);
        Assert.Equal(localFilePath, fakeClient.LastLocalPath);
        Assert.Equal("Song One.zip", fakeClient.LastDisplayName);
        Assert.Equal("encore", fakeClient.LastMetadata?.Source);
        Assert.Equal("encore-id-7", fakeClient.LastMetadata?.SourceId);
        Assert.Equal("Artist", fakeClient.LastMetadata?.Artist);
        Assert.Equal("Title", fakeClient.LastMetadata?.Title);
        Assert.Equal("Charter", fakeClient.LastMetadata?.Charter);
        Assert.Equal("rhythmverse", fakeClient.LastMetadata?.LibrarySource);
    }

    private sealed class FakeDesktopSyncApiClient : IDesktopSyncApiClient
    {
        public string? LastLocalPath { get; private set; }
        public string? LastDisplayName { get; private set; }
        public LocalIngestionUploadMetadata? LastMetadata { get; private set; }

        public Task<DesktopSyncPairClaimResponse> ClaimPairTokenAsync(string baseUrl, string pairCode, string? deviceLabel = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<DesktopSyncVersionResponse> GetVersionAsync(string baseUrl, string token, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<IngestionQueueItem>> GetIngestionsAsync(string baseUrl, string token, int limit = 100, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task TriggerRetryAsync(string baseUrl, string token, long ingestionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task TriggerInstallAsync(string baseUrl, string token, long ingestionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task TriggerOpenFolderAsync(string baseUrl, string token, long ingestionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<long> UploadIngestionFileAsync(
            string baseUrl,
            string token,
            string localPath,
            string displayName,
            LocalIngestionUploadMetadata? metadata = null,
            CancellationToken cancellationToken = default)
        {
            LastLocalPath = localPath;
            LastDisplayName = displayName;
            LastMetadata = metadata;
            return Task.FromResult(44L);
        }
    }
}
