using System.IO.Compression;

using ChartHub.Conversion;
using ChartHub.Conversion.Models;

using ChartHub.Server.Contracts;
using ChartHub.Server.Options;
using ChartHub.Server.Services;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ChartHub.Server.Tests;

public sealed class DownloadJobInstallServiceTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // ResolveDownloadedArtifactPath guard
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InstallJobWhenDownloadedPathEmptyThrows()
    {
        using var env = new TempEnvironment();
        DownloadJobInstallService sut = env.BuildService();
        DownloadJobResponse job = MakeJob(downloadedPath: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.InstallJobAsync(job));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Artifact missing on disk
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InstallJobWhenArtifactFileMissingThrows()
    {
        using var env = new TempEnvironment();
        DownloadJobInstallService sut = env.BuildService();
        DownloadJobResponse job = MakeJob(downloadedPath: Path.Combine(env.DownloadsDir, "doesnotexist.zip"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.InstallJobAsync(job));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Happy path — ZIP archive with a song.ini
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InstallJobZipWithSongIniExtractsAndReturnsInstalledPath()
    {
        using var env = new TempEnvironment();

        // Create a zip containing a song.ini with metadata.
        string zipPath = Path.Combine(env.DownloadsDir, "coolsong.zip");
        CreateZip(zipPath, [
            ("song.ini", "[song]\nartist = Test Artist\nname = Test Title\ncharter = Test Charter\n"),
            ("notes.chart", ""),
        ]);

        DownloadJobInstallService sut = env.BuildService(
            fileType: ServerInstallFileType.Zip);

        DownloadJobResponse job = MakeJob(downloadedPath: zipPath);
        DownloadJobInstallResult result = await sut.InstallJobAsync(job);

        Assert.True(Directory.Exists(result.InstalledPath));
        Assert.False(string.IsNullOrWhiteSpace(result.InstalledRelativePath));
        Assert.Equal("Test Artist", result.Metadata.Artist);
        Assert.Equal("Test Title", result.Metadata.Title);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ZIP with no song.ini falls back to fallback metadata
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InstallJobZipWithoutSongIniFallsBackToUnknownMetadata()
    {
        using var env = new TempEnvironment();

        string zipPath = Path.Combine(env.DownloadsDir, "track.zip");
        CreateZip(zipPath, [("notes.chart", "")]);

        DownloadJobInstallService sut = env.BuildService(
            fileType: ServerInstallFileType.Zip);

        DownloadJobResponse job = MakeJob(downloadedPath: zipPath);
        DownloadJobInstallResult result = await sut.InstallJobAsync(job);

        Assert.False(string.IsNullOrWhiteSpace(result.InstalledPath));
        Assert.Equal("Unknown Artist", result.Metadata.Artist);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Installed path is inside the configured CloneHero root
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InstallJobZipInstalledPathIsInsideCloneHeroRoot()
    {
        using var env = new TempEnvironment();

        string zipPath = Path.Combine(env.DownloadsDir, "mysong.zip");
        CreateZip(zipPath, [("notes.mid", "")]);

        DownloadJobInstallService sut = env.BuildService(
            fileType: ServerInstallFileType.Zip);

        DownloadJobResponse job = MakeJob(downloadedPath: zipPath);
        DownloadJobInstallResult result = await sut.InstallJobAsync(job);

        Assert.StartsWith(env.CloneHeroRoot, result.InstalledPath, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ZIP with a top-level subdirectory — files must land directly in final dir
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InstallJobZipWithTopLevelSubdirFilesLandDirectlyInInstalledPath()
    {
        using var env = new TempEnvironment();

        // Archive contains a top-level folder "Artist - Title/" as many RhythmVerse zips do.
        string zipPath = Path.Combine(env.DownloadsDir, "nested.zip");
        CreateZip(zipPath, [
            ("Artist - Title/song.ini", "[song]\nartist = Nested Artist\nname = Nested Title\ncharter = Nested Charter\n"),
            ("Artist - Title/notes.chart", ""),
        ]);

        DownloadJobInstallService sut = env.BuildService(fileType: ServerInstallFileType.Zip);
        DownloadJobResponse job = MakeJob(downloadedPath: zipPath);
        DownloadJobInstallResult result = await sut.InstallJobAsync(job);

        Assert.True(Directory.Exists(result.InstalledPath));

        // song.ini must be directly inside the installed path — no extra subdirectory.
        string[] directFiles = Directory.GetFiles(result.InstalledPath);
        Assert.Contains(directFiles, f => Path.GetFileName(f).Equals("song.ini", StringComparison.OrdinalIgnoreCase));

        // No subdirectory should remain inside the install folder.
        string[] subdirs = Directory.GetDirectories(result.InstalledPath);
        Assert.Empty(subdirs);
    }

    [Fact]
    public async Task InstallJobZipWithoutTopLevelSubdirFilesLandDirectlyInInstalledPath()
    {
        using var env = new TempEnvironment();

        // Archive has no top-level subdirectory — files are at root of zip.
        string zipPath = Path.Combine(env.DownloadsDir, "flat.zip");
        CreateZip(zipPath, [
            ("song.ini", "[song]\nartist = Flat Artist\nname = Flat Title\ncharter = Flat Charter\n"),
            ("notes.chart", ""),
        ]);

        DownloadJobInstallService sut = env.BuildService(fileType: ServerInstallFileType.Zip);
        DownloadJobResponse job = MakeJob(downloadedPath: zipPath);
        DownloadJobInstallResult result = await sut.InstallJobAsync(job);

        Assert.True(Directory.Exists(result.InstalledPath));

        string[] directFiles = Directory.GetFiles(result.InstalledPath);
        Assert.Contains(directFiles, f => Path.GetFileName(f).Equals("song.ini", StringComparison.OrdinalIgnoreCase));

        string[] subdirs = Directory.GetDirectories(result.InstalledPath);
        Assert.Empty(subdirs);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Unsupported file type throws
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InstallJobUnsupportedFileTypeThrows()
    {
        using var env = new TempEnvironment();

        string filePath = Path.Combine(env.DownloadsDir, "track.dat");
        await File.WriteAllBytesAsync(filePath, [0x00, 0x01, 0x02]);

        DownloadJobInstallService sut = env.BuildService(
            fileType: ServerInstallFileType.Unknown);

        DownloadJobResponse job = MakeJob(downloadedPath: filePath);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.InstallJobAsync(job));
    }

    [Fact]
    public async Task InstallJobEncryptedSngThrowsExplicitUnsupportedVariantMessage()
    {
        using var env = new TempEnvironment();

        string filePath = Path.Combine(env.DownloadsDir, "biology.sng");
        await File.WriteAllBytesAsync(filePath, [0xFF, 0xDB, 0x97, 0x17, 0x1F, 0x93, 0x28, 0x38]);

        DownloadJobInstallService sut = env.BuildService(
            fileType: ServerInstallFileType.EncryptedSng);

        DownloadJobResponse job = MakeJob(downloadedPath: filePath);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.InstallJobAsync(job));
        Assert.Equal("SNG artifact appears encrypted or uses an unsupported official variant.", ex.Message);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Cancellation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InstallJobCancelledBeforeTypeResolutionThrows()
    {
        using var env = new TempEnvironment();
        using var cts = new CancellationTokenSource();

        string zipPath = Path.Combine(env.DownloadsDir, "song.zip");
        CreateZip(zipPath, [("notes.chart", "")]);

        cts.Cancel();

        DownloadJobInstallService sut = env.BuildService(
            fileType: ServerInstallFileType.Zip,
            cancelOnTypeResolve: cts.Token);

        DownloadJobResponse job = MakeJob(downloadedPath: zipPath);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sut.InstallJobAsync(job, cts.Token));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Job log sink receives log entries during install
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InstallJobWritesLogEntriesToSink()
    {
        using var env = new TempEnvironment();

        string zipPath = Path.Combine(env.DownloadsDir, "logged.zip");
        CreateZip(zipPath, [("notes.chart", "")]);

        var sink = new CapturingJobLogSink();
        DownloadJobInstallService sut = env.BuildService(
            fileType: ServerInstallFileType.Zip,
            sink: sink);

        DownloadJobResponse job = MakeJob(downloadedPath: zipPath);
        await sut.InstallJobAsync(job);

        IReadOnlyList<JobLogEntry> entries = sink.GetLogs(job.JobId);
        Assert.NotEmpty(entries);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static DownloadJobResponse MakeJob(string? downloadedPath)
    {
        return new DownloadJobResponse
        {
            JobId = Guid.NewGuid(),
            Source = "rhythmverse",
            SourceId = "test-song-id",
            DisplayName = "Test Song",
            SourceUrl = "https://example.com/test",
            Stage = "Downloaded",
            ProgressPercent = 100,
            DownloadedPath = downloadedPath,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static void CreateZip(string zipPath, IReadOnlyList<(string Name, string Content)> entries)
    {
        using ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach ((string name, string content) in entries)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
            using StreamWriter writer = new(entry.Open());
            writer.Write(content);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test environment
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class TempEnvironment : IDisposable
    {
        public TempEnvironment()
        {
            string root = Path.Combine(Path.GetTempPath(), "charthub-install-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Root = root;
            DownloadsDir = Path.Combine(root, "downloads");
            StagingDir = Path.Combine(root, "staging");
            CloneHeroRoot = Path.Combine(root, "songs");
            Directory.CreateDirectory(DownloadsDir);
        }

        public string Root { get; }
        public string DownloadsDir { get; }
        public string StagingDir { get; }
        public string CloneHeroRoot { get; }

        public DownloadJobInstallService BuildService(
            ServerInstallFileType fileType = ServerInstallFileType.Zip,
            IJobLogSink? sink = null,
            CancellationToken cancelOnTypeResolve = default)
        {
            IOptions<ServerPathOptions> pathOptions = Microsoft.Extensions.Options.Options.Create(new ServerPathOptions
            {
                StagingDir = StagingDir,
                CloneHeroRoot = CloneHeroRoot,
            });

            return new DownloadJobInstallService(
                pathOptions,
                new StubWebHostEnvironment(Root),
                new StubFileTypeResolver(fileType, cancelOnTypeResolve),
                new StubConversionService(),
                new ServerSongIniMetadataParser(),
                new ServerCloneHeroDirectorySchemaService(),
                NullLogger<DownloadJobInstallService>.Instance,
                sink ?? new NullJobLogSink());
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Stubs
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class StubFileTypeResolver(
        ServerInstallFileType type,
        CancellationToken cancelAfterCall = default) : IServerInstallFileTypeResolver
    {
        public Task<ServerArtifactClassification> ClassifyAsync(string artifactPath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            cancelAfterCall.ThrowIfCancellationRequested();
            return Task.FromResult(new ServerArtifactClassification(type, type switch
            {
                ServerInstallFileType.Zip => ".zip",
                ServerInstallFileType.Rar => ".rar",
                ServerInstallFileType.SevenZip => ".7z",
                ServerInstallFileType.Con => ".rb3con",
                ServerInstallFileType.Sng => ".sng",
                ServerInstallFileType.EncryptedSng => ".sng",
                _ => string.Empty,
            }));
        }

        public Task<ServerInstallFileType> ResolveAsync(string artifactPath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            cancelAfterCall.ThrowIfCancellationRequested();
            return Task.FromResult(type);
        }
    }

    private sealed class StubConversionService : IConversionService
    {
        public Task<ConversionResult> ConvertAsync(string sourcePath, string outputRoot, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("CON conversion not supported in unit tests.");
    }

    private sealed class StubWebHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "test";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Testing";
    }

    private sealed class NullJobLogSink : IJobLogSink
    {
        public void Add(Guid jobId, LogLevel level, EventId eventId, string? category, string message, string? exception) { }
        public IReadOnlyList<JobLogEntry> GetLogs(Guid jobId) => [];
    }

    private sealed class CapturingJobLogSink : IJobLogSink
    {
        private readonly List<JobLogEntry> _entries = [];

        public void Add(Guid jobId, LogLevel level, EventId eventId, string? category, string message, string? exception)
            => _entries.Add(new JobLogEntry(DateTimeOffset.UtcNow, level.ToString(), eventId.Id, category, message, exception));

        public IReadOnlyList<JobLogEntry> GetLogs(Guid jobId) => _entries;
    }
}
