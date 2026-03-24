using System.Collections.ObjectModel;

using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using ChartHub.Models;
using ChartHub.Services;
using ChartHub.Tests.TestInfrastructure;
using ChartHub.Utilities;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public class DownloadViewModelTests
{
    [Fact]
    public async Task CheckAllCommand_AndItemSelection_UpdateCheckedStateFlags()
    {
        using var temp = new TemporaryDirectoryFixture("download-vm-checks");
        using AppGlobalSettings settings = CreateSettings(temp.RootPath);
        var ingestionCatalog = new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        var sut = new ViewModels.DownloadViewModel(
            settings,
            new SongInstallServiceStub(),
            ingestionCatalog,
            new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db")),
            new LocalFileDeletionService());

        try
        {
            sut.DownloadFiles.Add(CreateWatcherFile("alpha.zip", temp.GetPath("alpha.zip")));
            sut.DownloadFiles.Add(CreateWatcherFile("beta.zip", temp.GetPath("beta.zip")));

            sut.CheckAllCommand.Execute(null);

            Assert.True(sut.IsAllChecked);
            Assert.All(sut.DownloadFiles, file => Assert.True(file.Checked));
            Assert.True(sut.IsAnyChecked);

            sut.DownloadFiles[0].Checked = false;

            Assert.True(sut.IsAnyChecked);

            sut.DownloadFiles[1].Checked = false;

            Assert.False(sut.IsAnyChecked);
        }
        finally
        {
            await sut.DisposeAsync();
        }
    }

    [Fact]
    public async Task ToggleInstallLogCommand_TogglesExpandedStateAndLabel()
    {
        using var temp = new TemporaryDirectoryFixture("download-vm-log-toggle");
        using AppGlobalSettings settings = CreateSettings(temp.RootPath);
        var ingestionCatalog = new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        var sut = new ViewModels.DownloadViewModel(
            settings,
            new SongInstallServiceStub(),
            ingestionCatalog,
            new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db")),
            new LocalFileDeletionService());

        try
        {
            Assert.True(sut.IsInstallLogExpanded);
            Assert.Equal("Collapse Log", sut.InstallLogToggleText);

            sut.ToggleInstallLogCommand.Execute(null);

            Assert.False(sut.IsInstallLogExpanded);
            Assert.Equal("Expand Log", sut.InstallLogToggleText);

            sut.ToggleInstallLogCommand.Execute(null);

            Assert.True(sut.IsInstallLogExpanded);
            Assert.Equal("Collapse Log", sut.InstallLogToggleText);
        }
        finally
        {
            await sut.DisposeAsync();
        }
    }

    [Fact]
    public async Task InstallSongsCommand_SetsSummary_AndDismissHidesPanel()
    {
        using var temp = new TemporaryDirectoryFixture("download-vm-install-summary");
        using AppGlobalSettings settings = CreateSettings(temp.RootPath);
        string selectedFilePath = temp.GetPath("song-a.zip");
        await File.WriteAllTextAsync(selectedFilePath, "zip");
        var installStub = new SongInstallServiceStub
        {
            ResultPaths = [Path.Combine(temp.RootPath, "CloneHero", "Songs", "Song A")],
        };
        var ingestionCatalog = new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        var sut = new ViewModels.DownloadViewModel(
            settings,
            installStub,
            ingestionCatalog,
            new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db")),
            new LocalFileDeletionService());

        try
        {
            sut.DownloadFiles.Add(CreateWatcherFile("song-a.zip", selectedFilePath, checkedState: true));

            await sut.InstallSongsCommand();

            Assert.Equal("Installed 1 item successfully.", sut.InstallSummaryText);
            Assert.True(sut.HasInstallSummary);
            Assert.True(sut.IsInstallPanelVisible);
            Assert.False(sut.IsInstallActive);

            sut.DismissInstallPanelCommand.Execute(null);

            Assert.False(sut.IsInstallPanelVisible);
            Assert.False(sut.HasInstallSummary);
            Assert.Empty(sut.InstallLogItems);
        }
        finally
        {
            await sut.DisposeAsync();
        }
    }

    [Fact]
    public async Task InstallSongsCommand_OnCancellation_SetsCancelledSummary()
    {
        using var temp = new TemporaryDirectoryFixture("download-vm-install-cancel");
        using AppGlobalSettings settings = CreateSettings(temp.RootPath);
        string selectedFilePath = temp.GetPath("song-cancel.zip");
        await File.WriteAllTextAsync(selectedFilePath, "zip");
        var installStub = new SongInstallServiceStub
        {
            ThrowOnInstall = new OperationCanceledException(),
        };
        var ingestionCatalog = new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        var sut = new ViewModels.DownloadViewModel(
            settings,
            installStub,
            ingestionCatalog,
            new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db")),
            new LocalFileDeletionService());

        try
        {
            sut.DownloadFiles.Add(CreateWatcherFile("song-cancel.zip", selectedFilePath, checkedState: true));

            await sut.InstallSongsCommand();

            Assert.Equal("Install cancelled.", sut.InstallSummaryText);
            Assert.True(sut.IsInstallPanelVisible);
            Assert.False(sut.IsInstallActive);
        }
        finally
        {
            await sut.DisposeAsync();
        }
    }

    [Fact]
    public async Task DeleteSelectedDownloadsCommand_DeletesFileAndRemovesCatalogEntries()
    {
        using var temp = new TemporaryDirectoryFixture("download-vm-delete-selected");
        using AppGlobalSettings settings = CreateSettings(temp.RootPath);
        var ingestionCatalog = new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        var libraryCatalog = new LibraryCatalogService(Path.Combine(temp.RootPath, "library-catalog.db"));
        var sut = new ViewModels.DownloadViewModel(
            settings,
            new SongInstallServiceStub(),
            ingestionCatalog,
            libraryCatalog,
            new LocalFileDeletionService());

        string downloadFilePath = temp.GetPath("to-delete.zip");
        await File.WriteAllTextAsync(downloadFilePath, "zip-data");

        SongIngestionRecord ingestion = await ingestionCatalog.GetOrCreateIngestionAsync(
            source: LibrarySourceNames.RhythmVerse,
            sourceId: "delete-me-id",
            sourceLink: "https://example.com/delete-me");
        await ingestionCatalog.UpsertAssetAsync(new SongIngestionAssetEntry(
            IngestionId: ingestion.Id,
            AttemptId: null,
            AssetRole: IngestionAssetRole.Downloaded,
            Location: downloadFilePath,
            SizeBytes: null,
            ContentHash: null,
            RecordedAtUtc: DateTimeOffset.UtcNow));
        await libraryCatalog.UpsertAsync(new LibraryCatalogEntry(
            Source: LibrarySourceNames.RhythmVerse,
            SourceId: "delete-me-id",
            Title: "Delete Me",
            Artist: "Artist",
            Charter: "Charter",
            LocalPath: Path.Combine(temp.RootPath, "CloneHero", "Songs", "Delete Me"),
            AddedAtUtc: DateTimeOffset.UtcNow));

        try
        {
            sut.IngestionQueue.Add(new IngestionQueueItem
            {
                IngestionId = ingestion.Id,
                Source = LibrarySourceNames.RhythmVerse,
                SourceId = "delete-me-id",
                SourceLink = "https://example.com/delete-me",
                CurrentState = IngestionState.Downloaded,
                DownloadedLocation = downloadFilePath,
                DisplayName = "to-delete.zip",
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                Checked = true,
            });

            await sut.DeleteSelectedDownloadsCommand.ExecuteAsync(null);

            Assert.False(File.Exists(downloadFilePath));
            Assert.False(await libraryCatalog.IsInLibraryAsync(LibrarySourceNames.RhythmVerse, "delete-me-id"));
            Assert.Null(await ingestionCatalog.GetIngestionByIdAsync(ingestion.Id));
        }
        finally
        {
            await sut.DisposeAsync();
        }
    }

    private static WatcherFile CreateWatcherFile(string displayName, string filePath, bool checkedState = false)
    {
        return new WatcherFile(displayName, filePath, WatcherFileType.Zip, "icon.png", 100)
        {
            Checked = checkedState,
        };
    }

    private static AppGlobalSettings CreateSettings(string rootPath)
    {
        var orchestrator = new FakeSettingsOrchestrator(new AppConfigRoot
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
        });

        return new AppGlobalSettings(orchestrator);
    }

    private sealed class SongInstallServiceStub : ISongInstallService
    {
        public IReadOnlyList<string> ResultPaths { get; set; } = [];
        public Exception? ThrowOnInstall { get; set; }

        public Task<IReadOnlyList<string>> InstallSelectedDownloadsAsync(IEnumerable<string> selectedFilePaths, IProgress<InstallProgressUpdate>? progress = null, CancellationToken cancellationToken = default)
        {
            if (ThrowOnInstall is not null)
            {
                throw ThrowOnInstall;
            }

            return Task.FromResult(ResultPaths);
        }
    }

    private sealed class ResourceWatcherStub : IResourceWatcher
    {
        public ResourceWatcherStub(string directoryPath, ObservableCollection<WatcherFile> data)
        {
            DirectoryPath = directoryPath;
            Data = data;
        }

        public int LoadItemsCallCount { get; private set; }

        public string DirectoryPath { get; }
        public ObservableCollection<WatcherFile> Data { get; set; }
        public event EventHandler<string>? DirectoryNotFound
        {
            add { }
            remove { }
        }

        public void LoadItems()
        {
            LoadItemsCallCount++;
        }
    }

    private sealed class FakeSettingsOrchestrator : ISettingsOrchestrator
    {
        public FakeSettingsOrchestrator(AppConfigRoot current)
        {
            Current = current;
        }

        public AppConfigRoot Current { get; private set; }
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
