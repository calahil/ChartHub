using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using ChartHub.Services;
using ChartHub.Tests.TestInfrastructure;
using ChartHub.Utilities;
using ChartHub.ViewModels;

namespace ChartHub.Tests;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class CloneHeroViewModelTests
{
    [Fact]
    public void InitialState_ShowsStartupBlockingState()
    {
        using var temp = new TemporaryDirectoryFixture("clonehero-vm-initial");
        CloneHeroViewModel sut = CreateViewModel(temp.RootPath, new ImmediateReconciliationService());

        Assert.False(sut.HasInitialized);
        Assert.True(sut.ShowStartupBlockingState);
        Assert.False(sut.IsStartupScanInProgress);
    }

    [Fact]
    public async Task InitializeAsync_ShowsBlockingStateUntilReconciliationCompletes()
    {
        using var temp = new TemporaryDirectoryFixture("clonehero-vm-startup-block");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CloneHeroViewModel sut = CreateViewModel(temp.RootPath, new GatedReconciliationService(gate.Task));

        Task initTask = sut.InitializeAsync();

        Assert.True(SpinWait.SpinUntil(() => sut.IsStartupScanInProgress, TimeSpan.FromSeconds(2)));
        Assert.True(sut.ShowStartupBlockingState);

        gate.SetResult();
        await initTask;

        Assert.True(sut.HasInitialized);
        Assert.False(sut.IsStartupScanInProgress);
        Assert.False(sut.ShowStartupBlockingState);
    }

    [Fact]
    public async Task ReParseMetadataCommand_IsDisabled_WhenNoSongSelected()
    {
        using var temp = new TemporaryDirectoryFixture("clonehero-vm-reparse-guard");
        CloneHeroViewModel sut = CreateViewModel(temp.RootPath, new ImmediateReconciliationService());
        await sut.InitializeAsync();

        Assert.Null(sut.SelectedSong);
        Assert.False(sut.ReParseMetadataCommand.CanExecute(null));
    }

    [Fact]
    public async Task ReconcileThisSongCommand_IsDisabled_WhenNoSongSelected()
    {
        using var temp = new TemporaryDirectoryFixture("clonehero-vm-reconcilethis-guard");
        CloneHeroViewModel sut = CreateViewModel(temp.RootPath, new ImmediateReconciliationService());
        await sut.InitializeAsync();

        Assert.Null(sut.SelectedSong);
        Assert.False(sut.ReconcileThisSongCommand.CanExecute(null));
    }

    [Fact]
    public async Task InitializeAsync_LoadsArtistsAndFiltersSongsBySelectedArtist()
    {
        using var temp = new TemporaryDirectoryFixture("clonehero-vm-artist-filter");
        var catalog = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));

        await catalog.UpsertAsync(new LibraryCatalogEntry(
            Source: LibrarySourceNames.RhythmVerse,
            SourceId: "artist-a-song-1",
            Title: "Song One",
            Artist: "Artist A",
            Charter: "Charter A",
            LocalPath: Path.Combine(temp.RootPath, "songs", "Artist A", "Song One", "Charter A__rhythmverse"),
            AddedAtUtc: DateTimeOffset.UtcNow));

        await catalog.UpsertAsync(new LibraryCatalogEntry(
            Source: LibrarySourceNames.RhythmVerse,
            SourceId: "artist-b-song-1",
            Title: "Song Two",
            Artist: "Artist B",
            Charter: "Charter B",
            LocalPath: Path.Combine(temp.RootPath, "songs", "Artist B", "Song Two", "Charter B__rhythmverse"),
            AddedAtUtc: DateTimeOffset.UtcNow));

        var sut = new CloneHeroViewModel(catalog, new NoopDesktopPathOpener(), new ImmediateReconciliationService());
        await sut.InitializeAsync();

        Assert.Equal(2, sut.Artists.Count);
        Assert.Equal("Artist A", sut.SelectedArtist);
        Assert.Single(sut.Songs);
        Assert.Equal("Song One", sut.Songs[0].Title);

        sut.SelectedArtist = "Artist B";
        Assert.True(SpinWait.SpinUntil(() => sut.Songs.Count == 1 && sut.Songs[0].Title == "Song Two", TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task ReParseMetadataCommand_SetsStatusMessage_AfterCompletion()
    {
        using var temp = new TemporaryDirectoryFixture("clonehero-vm-reparse-status");
        var catalog = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));

        // Seed a catalog entry so SelectedSong gets populated after init
        string songDir = Path.Combine(temp.RootPath, "songs", "Artista", "Titulo", "Charter__rhythmverse");
        Directory.CreateDirectory(songDir);
        File.WriteAllText(Path.Combine(songDir, "song.ini"), "[song]\nartist = Artista\ntitle = Titulo\ncharter = Charter\n");
        await catalog.UpsertAsync(new LibraryCatalogEntry(
            Source: LibrarySourceNames.RhythmVerse, SourceId: "test-id",
            Title: "Titulo", Artist: "Artista", Charter: "Charter",
            LocalPath: songDir, AddedAtUtc: DateTimeOffset.UtcNow));

        var opener = new NoopDesktopPathOpener();
        var sut = new CloneHeroViewModel(catalog, opener, new ImmediateReconciliationService());
        await sut.InitializeAsync();

        // SelectedSong should now be set (at least one artist/song was seeded)
        Assert.NotNull(sut.SelectedSong);

        await sut.ReParseMetadataCommand.ExecuteAsync(null);

        Assert.Contains("Metadata updated", sut.ReconciliationStatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static CloneHeroViewModel CreateViewModel(string rootPath, ICloneHeroLibraryReconciliationService reconciliationService)
    {
        var catalog = new LibraryCatalogService(Path.Combine(rootPath, "library-catalog.db"));
        var opener = new NoopDesktopPathOpener();
        return new CloneHeroViewModel(catalog, opener, reconciliationService);
    }

    private sealed class NoopDesktopPathOpener : IDesktopPathOpener
    {
        public Task OpenDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class ImmediateReconciliationService : ICloneHeroLibraryReconciliationService
    {
        public Task<CloneHeroReconciliationResult> ReconcileAsync(IProgress<CloneHeroReconciliationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CloneHeroReconciliationResult(Scanned: 0, Updated: 0, Renamed: 0, Failed: 0));
        }

        public Task<bool> ReconcileSongDirectoryAsync(string songDirectory, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task<bool> ReParseMetadataAsync(string songDirectory, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }
    }

    private sealed class GatedReconciliationService(Task gate) : ICloneHeroLibraryReconciliationService
    {
        public async Task<CloneHeroReconciliationResult> ReconcileAsync(IProgress<CloneHeroReconciliationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken);
            return new CloneHeroReconciliationResult(Scanned: 1, Updated: 1, Renamed: 0, Failed: 0);
        }

        public Task<bool> ReconcileSongDirectoryAsync(string songDirectory, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task<bool> ReParseMetadataAsync(string songDirectory, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }
    }
}
