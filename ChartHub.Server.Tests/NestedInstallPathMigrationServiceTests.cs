using ChartHub.Server.Contracts;
using ChartHub.Server.Services;

using Microsoft.Extensions.Logging.Abstractions;

namespace ChartHub.Server.Tests;

public sealed class NestedInstallPathMigrationServiceTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // No installed jobs — no-op
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartAsyncWithNoInstalledJobsDoesNothing()
    {
        var store = new StubDownloadJobStore([]);
        var sut = new NestedInstallPathMigrationService(store, NullLogger<NestedInstallPathMigrationService>.Instance);

        await sut.StartAsync(CancellationToken.None);

        Assert.Empty(store.DeletedJobIds);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Nested path — files are moved up, subdirectory removed
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartAsyncFlattensNestedInstalledDirectory()
    {
        using var tmp = new TempDirectory();
        string installedPath = tmp.CreateSubDir("Arcade Fire", "Creature Comfort", "Debugmod12__rhythmverse");

        // Archive left an extra subdirectory inside.
        string nestedDir = Path.Combine(installedPath, "Arcade Fire - Creature Comfort");
        Directory.CreateDirectory(nestedDir);
        File.WriteAllText(Path.Combine(nestedDir, "song.ini"), "[song]");
        File.WriteAllText(Path.Combine(nestedDir, "notes.chart"), "");

        DownloadJobResponse job = MakeInstalledJob(installedPath);
        StubDownloadJobStore store = new([job]);
        NestedInstallPathMigrationService sut = new(store, NullLogger<NestedInstallPathMigrationService>.Instance);

        await sut.StartAsync(CancellationToken.None);

        // song.ini must now be directly in installedPath.
        Assert.True(File.Exists(Path.Combine(installedPath, "song.ini")));
        Assert.True(File.Exists(Path.Combine(installedPath, "notes.chart")));

        // Nested subdirectory must be gone.
        Assert.False(Directory.Exists(nestedDir));

        // No jobs deleted (only one, no duplicates).
        Assert.Empty(store.DeletedJobIds);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Clean install — not modified
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartAsyncDoesNotModifyAlreadyFlatDirectory()
    {
        using var tmp = new TempDirectory();
        string installedPath = tmp.CreateSubDir("Artist", "Song", "Charter__rhythmverse");
        File.WriteAllText(Path.Combine(installedPath, "song.ini"), "[song]");
        File.WriteAllText(Path.Combine(installedPath, "notes.chart"), "");

        DownloadJobResponse job = MakeInstalledJob(installedPath);
        StubDownloadJobStore store = new([job]);
        NestedInstallPathMigrationService sut = new(store, NullLogger<NestedInstallPathMigrationService>.Instance);

        await sut.StartAsync(CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(installedPath, "song.ini")));
        Assert.Empty(store.DeletedJobIds);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Duplicate jobs — _N suffix deleted, original kept
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartAsyncDeletesDuplicateJobWithNumericSuffix()
    {
        using var tmp = new TempDirectory();
        string originalPath = tmp.CreateSubDir("A", "B", "Charter__rhythmverse");
        string duplicatePath = tmp.CreateSubDir("A", "B", "Charter__rhythmverse_2");

        // Both have their chart files nested (bug scenario).
        CreateNestedChart(originalPath, "A - B");
        CreateNestedChart(duplicatePath, "A - B");

        DownloadJobResponse original = MakeInstalledJob(originalPath, artist: "A", title: "B", charter: "Charter", source: "rhythmverse", createdAt: DateTimeOffset.UtcNow.AddMinutes(-10));
        DownloadJobResponse duplicate = MakeInstalledJob(duplicatePath, artist: "A", title: "B", charter: "Charter", source: "rhythmverse", createdAt: DateTimeOffset.UtcNow);

        StubDownloadJobStore store = new([original, duplicate]);
        NestedInstallPathMigrationService sut = new(store, NullLogger<NestedInstallPathMigrationService>.Instance);

        await sut.StartAsync(CancellationToken.None);

        // Duplicate job (with _2 suffix) was deleted from store.
        Assert.Contains(duplicate.JobId, store.DeletedJobIds);
        Assert.DoesNotContain(original.JobId, store.DeletedJobIds);

        // Duplicate directory removed from filesystem.
        Assert.False(Directory.Exists(duplicatePath));

        // Original directory still exists.
        Assert.True(Directory.Exists(originalPath));
    }

    [Fact]
    public async Task StartAsyncWhenOnlyDuplicateExistsKeepsLowestSuffix()
    {
        using var tmp = new TempDirectory();
        string path2 = tmp.CreateSubDir("A", "B", "Charter__rhythmverse_2");
        string path3 = tmp.CreateSubDir("A", "B", "Charter__rhythmverse_3");

        CreateNestedChart(path2, "A - B");
        CreateNestedChart(path3, "A - B");

        DownloadJobResponse job2 = MakeInstalledJob(path2, artist: "A", title: "B", charter: "Charter", source: "rhythmverse");
        DownloadJobResponse job3 = MakeInstalledJob(path3, artist: "A", title: "B", charter: "Charter", source: "rhythmverse");

        StubDownloadJobStore store = new([job2, job3]);
        NestedInstallPathMigrationService sut = new(store, NullLogger<NestedInstallPathMigrationService>.Instance);

        await sut.StartAsync(CancellationToken.None);

        // Highest suffix (_3) deleted, lowest (_2) kept.
        Assert.Contains(job3.JobId, store.DeletedJobIds);
        Assert.DoesNotContain(job2.JobId, store.DeletedJobIds);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Missing directory — skipped gracefully
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartAsyncSkipsJobWhoseDirectoryDoesNotExist()
    {
        DownloadJobResponse job = MakeInstalledJob("/does/not/exist/Charter__rhythmverse");
        StubDownloadJobStore store = new([job]);
        NestedInstallPathMigrationService sut = new(store, NullLogger<NestedInstallPathMigrationService>.Instance);

        // Should not throw.
        await sut.StartAsync(CancellationToken.None);
        Assert.Empty(store.DeletedJobIds);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static DownloadJobResponse MakeInstalledJob(
        string installedPath,
        string artist = "Artist",
        string title = "Title",
        string charter = "Charter",
        string source = "rhythmverse",
        DateTimeOffset? createdAt = null)
    {
        return new DownloadJobResponse
        {
            JobId = Guid.NewGuid(),
            Source = source,
            SourceId = "test-id",
            DisplayName = $"{artist} - {title}",
            SourceUrl = "https://example.com",
            Stage = "Installed",
            ProgressPercent = 100,
            InstalledPath = installedPath,
            InstalledRelativePath = installedPath,
            Artist = artist,
            Title = title,
            Charter = charter,
            CreatedAtUtc = createdAt ?? DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static void CreateNestedChart(string installedPath, string folderName)
    {
        string nested = Path.Combine(installedPath, folderName);
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "song.ini"), "[song]");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Stubs
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class StubDownloadJobStore(IReadOnlyList<DownloadJobResponse> jobs) : IDownloadJobStore
    {
        private readonly List<DownloadJobResponse> _jobs = [.. jobs];
        public List<Guid> DeletedJobIds { get; } = [];

        public IReadOnlyList<DownloadJobResponse> List() => _jobs;

        public void DeleteJob(Guid jobId)
        {
            DeletedJobIds.Add(jobId);
            _jobs.RemoveAll(j => j.JobId == jobId);
        }

        // Unused members — throw to surface unexpected calls.
        public DownloadJobResponse Create(CreateDownloadJobRequest request) => throw new NotSupportedException();
        public bool TryGet(Guid jobId, out DownloadJobResponse? response) => throw new NotSupportedException();
        public void QueueRetry(Guid jobId) => throw new NotSupportedException();
        public void RequestCancel(Guid jobId) => throw new NotSupportedException();
        public bool IsCancelRequested(Guid jobId) => throw new NotSupportedException();
        public DownloadJobResponse? TryClaimNextQueuedJob() => throw new NotSupportedException();
        public void UpdateProgress(Guid jobId, string stage, double progressPercent) => throw new NotSupportedException();
        public void SetDownloadedArtifact(Guid jobId, string downloadedPath, string fileType) => throw new NotSupportedException();
        public void UpdateFileType(Guid jobId, string fileType) => throw new NotSupportedException();
        public IReadOnlyList<DownloadJobResponse> ListDownloadedWithoutFileType() => throw new NotSupportedException();
        public void MarkStaged(Guid jobId, string stagedPath) => throw new NotSupportedException();
        public void MarkInstalled(Guid jobId, string installedPath, string? installedRelativePath = null, string? artist = null, string? title = null, string? charter = null, string? sourceMd5 = null, string? sourceChartHash = null) => throw new NotSupportedException();
        public void MarkCancelled(Guid jobId) => throw new NotSupportedException();
        public void MarkFailed(Guid jobId, string errorMessage) => throw new NotSupportedException();
        public int DeleteFinishedOlderThan(DateTimeOffset thresholdUtc) => throw new NotSupportedException();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Temp directory helper
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class TempDirectory : IDisposable
    {
        private readonly string _root;

        public TempDirectory()
        {
            _root = Path.Combine(Path.GetTempPath(), "charthub-migration-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public string CreateSubDir(params string[] segments)
        {
            string path = Path.Combine([_root, .. segments]);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
