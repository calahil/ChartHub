using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using ChartHub.Services;
using ChartHub.Tests.TestInfrastructure;
using ChartHub.Utilities;

namespace ChartHub.Tests;

[Trait(TestCategories.Category, TestCategories.IntegrationLite)]
public class CloneHeroLibraryReconciliationServiceTests
{
    [Fact]
    public async Task ReconcileAsync_MovesUnmanagedDirectoryToQuarantine_AndSkipsCatalogUpsert()
    {
        using var temp = new TemporaryDirectoryFixture("clonehero-reconcile-import");
        var settings = CreateSettings(temp.RootPath);
        var songsRoot = settings.CloneHeroSongsDir;

        var legacyDir = Path.Combine(songsRoot, "legacy-folder");
        Directory.CreateDirectory(legacyDir);
        await File.WriteAllTextAsync(Path.Combine(legacyDir, "song.ini"), """
[song]
name = Song Title
artist = Artist Name
charter = Charter Name
""");
        await File.WriteAllTextAsync(Path.Combine(legacyDir, "notes.chart"), "chart-data");

        var libraryCatalog = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        var sut = new CloneHeroLibraryReconciliationService(
            settings,
            libraryCatalog,
            new SongIniMetadataParser(),
            new CloneHeroDirectorySchemaService());

        var result = await sut.ReconcileAsync();

        var quarantineRoot = Path.Combine(settings.CloneHeroDataDir, "Quarantine");
        Assert.True(Directory.Exists(quarantineRoot));
        Assert.Single(Directory.GetDirectories(quarantineRoot));
        Assert.False(Directory.Exists(legacyDir));

        Assert.Equal(1, result.Scanned);
        Assert.Equal(1, result.Updated);
        Assert.Equal(1, result.Renamed);
        Assert.Equal(0, result.Failed);

        var remainingEntries = await libraryCatalog.GetEntriesByArtistAsync("Artist Name");
        Assert.Empty(remainingEntries);
    }

    [Fact]
    public async Task ReconcileSongDirectoryAsync_PreservesKnownSourceFromExistingCatalogEntry()
    {
        using var temp = new TemporaryDirectoryFixture("clonehero-reconcile-preserve-source");
        var settings = CreateSettings(temp.RootPath);
        var songsRoot = settings.CloneHeroSongsDir;

        var legacyDir = Path.Combine(songsRoot, "legacy-folder-rv");
        Directory.CreateDirectory(legacyDir);
        await File.WriteAllTextAsync(Path.Combine(legacyDir, "song.ini"), """
[song]
name = Song Title
artist = Artist Name
charter = Charter Name
""");
        await File.WriteAllTextAsync(Path.Combine(legacyDir, "notes.chart"), "chart-data");

        var libraryCatalog = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        var rhythmVerseSourceId = LibraryIdentityService.BuildSourceKey(LibrarySourceNames.RhythmVerse, "rv-42");
        await libraryCatalog.UpsertAsync(new LibraryCatalogEntry(
            Source: LibrarySourceNames.RhythmVerse,
            SourceId: rhythmVerseSourceId,
            Title: "Song Title",
            Artist: "Artist Name",
            Charter: "Charter Name",
            LocalPath: legacyDir,
            AddedAtUtc: DateTimeOffset.UtcNow));

        var sut = new CloneHeroLibraryReconciliationService(
            settings,
            libraryCatalog,
            new SongIniMetadataParser(),
            new CloneHeroDirectorySchemaService());

        var updated = await sut.ReconcileSongDirectoryAsync(legacyDir);

        Assert.True(updated);

        var expectedPath = Path.Combine(songsRoot, "Artist Name", "Song Title", "Charter Name__rhythmverse");
        Assert.True(Directory.Exists(expectedPath));
        Assert.False(Directory.Exists(legacyDir));

        var entry = await libraryCatalog.GetEntryByLocalPathAsync(expectedPath);
        Assert.NotNull(entry);
        Assert.Equal(LibrarySourceNames.RhythmVerse, entry!.Source);
        Assert.Equal(rhythmVerseSourceId, entry.SourceId);
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
