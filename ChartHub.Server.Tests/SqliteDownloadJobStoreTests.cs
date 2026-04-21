using ChartHub.Server.Contracts;
using ChartHub.Server.Options;
using ChartHub.Server.Services;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
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

    [Fact]
    public void SetDownloadedArtifactSetsProgressTo100AndPersistsFileType()
    {
        SqliteDownloadJobStore sut = BuildStore();
        DownloadJobResponse created = sut.Create(new CreateDownloadJobRequest
        {
            Source = "rhythmverse",
            SourceId = "abc",
            DisplayName = "Track",
            SourceUrl = "https://example.com/track.zip",
        });

        sut.SetDownloadedArtifact(created.JobId, "/downloads/track.rb3con", ServerInstallFileType.Con.ToString());

        Assert.True(sut.TryGet(created.JobId, out DownloadJobResponse? updated));
        Assert.NotNull(updated);
        Assert.Equal("Downloaded", updated!.Stage);
        Assert.Equal(100, updated.ProgressPercent);
        Assert.Equal("/downloads/track.rb3con", updated.DownloadedPath);
        Assert.Equal(ServerInstallFileType.Con.ToString(), updated.FileType);
    }

    [Fact]
    public void MarkInstalledPersistsNormalizedMetadata()
    {
        SqliteDownloadJobStore sut = BuildStore();
        DownloadJobResponse created = sut.Create(new CreateDownloadJobRequest
        {
            Source = "encore",
            SourceId = "encore|chartId=42|md5=abcd",
            DisplayName = "Encore Track",
            SourceUrl = "https://example.com/encore/track.zip",
        });

        sut.MarkInstalled(
            created.JobId,
            "/clonehero/Artist/Title/Charter__encore",
            "Artist/Title/Charter__encore",
            "Artist",
            "Title",
            "Charter",
            "abcd",
            null);

        Assert.True(sut.TryGet(created.JobId, out DownloadJobResponse? updated));
        Assert.NotNull(updated);
        Assert.Equal("Installed", updated!.Stage);
        Assert.Equal("Artist", updated.Artist);
        Assert.Equal("Title", updated.Title);
        Assert.Equal("Charter", updated.Charter);
        Assert.Equal("abcd", updated.SourceMd5);
        Assert.Equal("Artist/Title/Charter__encore", updated.InstalledRelativePath);
    }

    [Fact]
    public void EnsureSchemaMigratesLegacyStagedAndCompletedStages()
    {
        Directory.CreateDirectory(_tempRoot);
        string dbPath = Path.Combine(_tempRoot, "charthub-server.db");
        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        using (SqliteConnection connection = new(connectionString))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE download_jobs (
                    job_id TEXT PRIMARY KEY,
                    source TEXT NOT NULL,
                    source_id TEXT NOT NULL,
                    display_name TEXT NOT NULL,
                    source_url TEXT NOT NULL,
                    stage TEXT NOT NULL,
                    progress_percent REAL NOT NULL,
                    cancel_requested INTEGER NOT NULL DEFAULT 0,
                    downloaded_path TEXT NULL,
                    staged_path TEXT NULL,
                    installed_path TEXT NULL,
                    error TEXT NULL,
                    created_at_utc TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL,
                    completed_at_utc TEXT NULL
                );
                """;
            command.ExecuteNonQuery();

            DateTimeOffset now = DateTimeOffset.UtcNow;

            using SqliteCommand stagedInsert = connection.CreateCommand();
            stagedInsert.CommandText = """
                INSERT INTO download_jobs (
                    job_id, source, source_id, display_name, source_url, stage, progress_percent,
                    cancel_requested, downloaded_path, staged_path, installed_path, error,
                    created_at_utc, updated_at_utc, completed_at_utc)
                VALUES (
                    $jobId, 'rhythmverse', 'legacy-1', 'Legacy Staged', 'https://example.test/a.zip', 'Staged', 90,
                    0, NULL, '/tmp/staged-a.zip', NULL, NULL,
                    $createdAtUtc, $updatedAtUtc, NULL);
                """;
            stagedInsert.Parameters.AddWithValue("$jobId", Guid.NewGuid().ToString("D"));
            stagedInsert.Parameters.AddWithValue("$createdAtUtc", now.ToString("O"));
            stagedInsert.Parameters.AddWithValue("$updatedAtUtc", now.ToString("O"));
            stagedInsert.ExecuteNonQuery();

            using SqliteCommand completedInsert = connection.CreateCommand();
            completedInsert.CommandText = """
                INSERT INTO download_jobs (
                    job_id, source, source_id, display_name, source_url, stage, progress_percent,
                    cancel_requested, downloaded_path, staged_path, installed_path, error,
                    created_at_utc, updated_at_utc, completed_at_utc)
                VALUES (
                    $jobId, 'rhythmverse', 'legacy-2', 'Legacy Completed', 'https://example.test/b.zip', 'Completed', 100,
                    0, '/tmp/download-b.zip', NULL, '/tmp/install-b', NULL,
                    $createdAtUtc, $updatedAtUtc, $completedAtUtc);
                """;
            completedInsert.Parameters.AddWithValue("$jobId", Guid.NewGuid().ToString("D"));
            completedInsert.Parameters.AddWithValue("$createdAtUtc", now.ToString("O"));
            completedInsert.Parameters.AddWithValue("$updatedAtUtc", now.ToString("O"));
            completedInsert.Parameters.AddWithValue("$completedAtUtc", now.ToString("O"));
            completedInsert.ExecuteNonQuery();
        }

        SqliteDownloadJobStore sut = BuildStore();
        IReadOnlyList<DownloadJobResponse> all = sut.List();

        DownloadJobResponse migratedStaged = Assert.Single(all, job => job.SourceId == "legacy-1");
        Assert.Equal("Downloaded", migratedStaged.Stage);
        Assert.Equal(100, migratedStaged.ProgressPercent);
        Assert.Equal("/tmp/staged-a.zip", migratedStaged.DownloadedPath);

        DownloadJobResponse migratedCompleted = Assert.Single(all, job => job.SourceId == "legacy-2");
        Assert.Equal("Installed", migratedCompleted.Stage);
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
