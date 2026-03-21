using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using ChartHub.Services;
using ChartHub.Tests.TestInfrastructure;
using ChartHub.Utilities;
using System.IO.Compression;

namespace ChartHub.Tests;

[Trait(TestCategories.Category, TestCategories.IntegrationLite)]
public class SongInstallServiceTests
{
    [Fact]
    public async Task InstallSelectedDownloadsAsync_ArchiveInstall_PopulatesMetadataAndCanonicalDirectory()
    {
        using var temp = new TemporaryDirectoryFixture("song-install-archive-canonical");
        var settings = CreateSettings(temp.RootPath);

        var archivePath = Path.Combine(settings.DownloadDir, "archive-song.zip");
        CreateArchiveWithSongIni(
            archivePath,
            "Artist Zip",
            "Title Zip",
            "Charter Zip");

        var ingestionCatalog = new SongIngestionCatalogService(Path.Combine(temp.RootPath, "ingestion-catalog.db"));
        var libraryCatalog = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        var stateMachine = new SongIngestionStateMachine();

        var ingestion = await ingestionCatalog.GetOrCreateIngestionAsync(
            source: LibrarySourceNames.Encore,
            sourceId: "encore-zip-123",
            sourceLink: "https://encore.example/songs/123");

        await ingestionCatalog.UpsertAssetAsync(new SongIngestionAssetEntry(
            IngestionId: ingestion.Id,
            AttemptId: null,
            AssetRole: IngestionAssetRole.Downloaded,
            Location: archivePath,
            SizeBytes: null,
            ContentHash: null,
            RecordedAtUtc: DateTimeOffset.UtcNow));

        var sut = new SongInstallService(
            settings,
            ingestionCatalog,
            stateMachine,
            new FakeOnyxPipelineService(Path.Combine(temp.RootPath, "unused"), SongMetadata.Unknown),
            new SongIniMetadataParser(),
            new CloneHeroDirectorySchemaService(),
            libraryCatalog);

        var installedDirectories = await sut.InstallSelectedDownloadsAsync([archivePath]);

        var installedDirectory = Assert.Single(installedDirectories);
        Assert.Contains($"{Path.DirectorySeparatorChar}Artist Zip{Path.DirectorySeparatorChar}", installedDirectory, StringComparison.Ordinal);
        Assert.Contains($"{Path.DirectorySeparatorChar}Title Zip{Path.DirectorySeparatorChar}", installedDirectory, StringComparison.Ordinal);
        Assert.Contains("Charter Zip__encore", installedDirectory, StringComparison.Ordinal);
        Assert.True(Directory.Exists(installedDirectory));
        Assert.True(File.Exists(Path.Combine(installedDirectory, "song.ini")));
        Assert.True(File.Exists(Path.Combine(installedDirectory, "notes.chart")));

        var entry = await libraryCatalog.GetEntryByLocalPathAsync(installedDirectory);
        Assert.NotNull(entry);
        Assert.Equal(LibrarySourceNames.Encore, entry!.Source);
        Assert.Equal("Artist Zip", entry.Artist);
        Assert.Equal("Title Zip", entry.Title);
        Assert.Equal("Charter Zip", entry.Charter);
    }

    [Fact]
    public async Task InstallSelectedDownloadsAsync_OnyxWithoutSongIni_UsesYamlMetadataForCatalogEntry()
    {
        using var temp = new TemporaryDirectoryFixture("song-install-onyx-yaml-fallback");
        var settings = CreateSettings(temp.RootPath);

        var downloadFile = Path.Combine(settings.DownloadDir, "yaml-only.con");
        await File.WriteAllTextAsync(downloadFile, "dummy");

        var onyxInstallRoot = Path.Combine(settings.OutputDir, "onyx", "job", "produced");
        Directory.CreateDirectory(onyxInstallRoot);
        await File.WriteAllTextAsync(Path.Combine(onyxInstallRoot, "notes.chart"), "chart-data");

        var ingestionCatalog = new SongIngestionCatalogService(Path.Combine(temp.RootPath, "ingestion-catalog.db"));
        var libraryCatalog = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        var stateMachine = new SongIngestionStateMachine();

        var ingestion = await ingestionCatalog.GetOrCreateIngestionAsync(
            source: LibrarySourceNames.RhythmVerse,
            sourceId: "rv-123",
            sourceLink: "https://rhythmverse.co/song/rv-123");

        await ingestionCatalog.UpsertAssetAsync(new SongIngestionAssetEntry(
            IngestionId: ingestion.Id,
            AttemptId: null,
            AssetRole: IngestionAssetRole.Downloaded,
            Location: downloadFile,
            SizeBytes: null,
            ContentHash: null,
            RecordedAtUtc: DateTimeOffset.UtcNow));

        var onyxMetadata = new SongMetadata("Yaml Artist", "Yaml Title", "Yaml Charter");
        var onyxPipeline = new FakeOnyxPipelineService(onyxInstallRoot, onyxMetadata);

        var sut = new SongInstallService(
            settings,
            ingestionCatalog,
            stateMachine,
            onyxPipeline,
            new SongIniMetadataParser(),
            new CloneHeroDirectorySchemaService(),
            libraryCatalog);

        var installedDirectories = await sut.InstallSelectedDownloadsAsync([downloadFile]);

        var installedDirectory = Assert.Single(installedDirectories);
        Assert.Contains($"{Path.DirectorySeparatorChar}Yaml Artist{Path.DirectorySeparatorChar}", installedDirectory, StringComparison.Ordinal);
        Assert.Contains($"{Path.DirectorySeparatorChar}Yaml Title{Path.DirectorySeparatorChar}", installedDirectory, StringComparison.Ordinal);
        Assert.Contains("Yaml Charter__rhythmverse", installedDirectory, StringComparison.Ordinal);

        var entry = await libraryCatalog.GetEntryByLocalPathAsync(installedDirectory);
        Assert.NotNull(entry);
        Assert.Equal(LibrarySourceNames.RhythmVerse, entry!.Source);
        Assert.Equal("Yaml Artist", entry.Artist);
        Assert.Equal("Yaml Title", entry.Title);
        Assert.Equal("Yaml Charter", entry.Charter);
    }

    private static AppGlobalSettings CreateSettings(string rootPath)
    {
        var config = new AppConfigRoot
        {
            Runtime = new RuntimeAppConfig
            {
                TempDirectory = Path.Combine(rootPath, "Temp"),
                DownloadDirectory = Path.Combine(rootPath, "Downloads"),
                StagingDirectory = Path.Combine(rootPath, "Staging"),
                OutputDirectory = Path.Combine(rootPath, "Output"),
                CloneHeroDataDirectory = Path.Combine(rootPath, "CloneHero"),
                CloneHeroSongDirectory = Path.Combine(rootPath, "CloneHero", "Songs"),
            },
        };

        foreach (var dir in new[]
        {
            config.Runtime.TempDirectory,
            config.Runtime.DownloadDirectory,
            config.Runtime.StagingDirectory,
            config.Runtime.OutputDirectory,
            config.Runtime.CloneHeroDataDirectory,
            config.Runtime.CloneHeroSongDirectory,
        })
        {
            Directory.CreateDirectory(dir);
        }

        return new AppGlobalSettings(new FakeSettingsOrchestrator(config));
    }

    private static void CreateArchiveWithSongIni(string archivePath, string artist, string title, string charter)
    {
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);

        var songIni = archive.CreateEntry("song.ini");
        using (var writer = new StreamWriter(songIni.Open()))
        {
            writer.Write($"[song]\nartist = {artist}\nname = {title}\ncharter = {charter}\n");
        }

        var notes = archive.CreateEntry("notes.chart");
        using var notesWriter = new StreamWriter(notes.Open());
        notesWriter.Write("chart-data");
    }

    private sealed class FakeOnyxPipelineService(string producedInstallDirectory, SongMetadata metadata) : IOnyxPipelineService
    {
        public Task<OnyxInstallResult> InstallAsync(
            string songPath,
            string sourceSuffix,
            IProgress<InstallProgressUpdate>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new OnyxInstallResult(
                producedInstallDirectory,
                Path.Combine(Path.GetDirectoryName(producedInstallDirectory)!, "import"),
                Path.Combine(Path.GetDirectoryName(producedInstallDirectory)!, "build"),
                metadata));
        }
    }

    private sealed class FakeSettingsOrchestrator(AppConfigRoot current) : ISettingsOrchestrator
    {
        public AppConfigRoot Current { get; private set; } = current;
        public event Action<AppConfigRoot>? SettingsChanged;

        public Task<ConfigValidationResult> UpdateAsync(Action<AppConfigRoot> update, CancellationToken cancellationToken = default)
        {
            update(Current);
            SettingsChanged?.Invoke(Current);
            return Task.FromResult(ConfigValidationResult.Success);
        }

        public Task ReloadAsync(CancellationToken cancellationToken = default)
        {
            SettingsChanged?.Invoke(Current);
            return Task.CompletedTask;
        }
    }
}
