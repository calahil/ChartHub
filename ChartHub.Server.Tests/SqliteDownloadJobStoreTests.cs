using ChartHub.Server.Contracts;
using ChartHub.Server.Options;
using ChartHub.Server.Services;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ChartHub.Server.Tests;

public sealed class SqliteDownloadJobStoreTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"charthub-server-tests-{Guid.NewGuid():N}");

    [Fact]
    public void CreateThenClaimUpdatesStage()
    {
        SqliteDownloadJobStore sut = BuildStore();

        DownloadJobResponse created = sut.Create(new CreateDownloadJobRequest
        {
            Source = "rhythmverse",
            SourceId = "abc",
            DisplayName = "Track",
            SourceUrl = "https://example.com/track.zip",
        });

        DownloadJobResponse? claimed = sut.TryClaimNextQueuedJob();

        Assert.NotNull(claimed);
        Assert.Equal(created.JobId, claimed.JobId);
        Assert.Equal("ResolvingSource", claimed.Stage);
    }

    [Fact]
    public void RetryAndCancelFlagsArePersisted()
    {
        SqliteDownloadJobStore sut = BuildStore();
        DownloadJobResponse created = sut.Create(new CreateDownloadJobRequest
        {
            Source = "rhythmverse",
            SourceId = "abc",
            DisplayName = "Track",
            SourceUrl = "https://example.com/track.zip",
        });

        sut.RequestCancel(created.JobId);
        bool cancelRequested = sut.IsCancelRequested(created.JobId);
        sut.QueueRetry(created.JobId);
        bool cancelAfterRetry = sut.IsCancelRequested(created.JobId);

        Assert.True(cancelRequested);
        Assert.False(cancelAfterRetry);
    }

    [Fact]
    public void MarkInstalledAndCleanupRemovesOldCompleted()
    {
        SqliteDownloadJobStore sut = BuildStore();
        DownloadJobResponse created = sut.Create(new CreateDownloadJobRequest
        {
            Source = "rhythmverse",
            SourceId = "abc",
            DisplayName = "Track",
            SourceUrl = "https://example.com/track.zip",
        });

        sut.MarkInstalled(created.JobId, "/clonehero/track");

        int removed = sut.DeleteFinishedOlderThan(DateTimeOffset.UtcNow.AddMinutes(1));
        bool stillExists = sut.TryGet(created.JobId, out _);

        Assert.Equal(1, removed);
        Assert.False(stillExists);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private SqliteDownloadJobStore BuildStore()
    {
        Directory.CreateDirectory(_tempRoot);
        string dbPath = Path.Combine(_tempRoot, "charthub-server.db");

        return new SqliteDownloadJobStore(
            Microsoft.Extensions.Options.Options.Create(new ServerPathOptions { SqliteDbPath = dbPath }),
            new TestHostEnvironment(_tempRoot));
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "ChartHub.Server.Tests";

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string WebRootPath { get; set; } = contentRootPath;

        public string EnvironmentName { get; set; } = Environments.Development;

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);
    }
}
