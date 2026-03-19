using System.Collections.ObjectModel;
using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using ChartHub.Models;
using ChartHub.Services;
using ChartHub.Services.Transfers;
using ChartHub.Utilities;
using ChartHub.Tests.TestInfrastructure;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public class DownloadViewModelTests
{
    [Fact]
    public async Task CheckAllCommand_AndItemSelection_UpdateCheckedStateFlags()
    {
        using var temp = new TemporaryDirectoryFixture("download-vm-checks");
        using var settings = CreateSettings(temp.RootPath);
        var transfer = new TransferOrchestratorSpy();
        var sut = new ViewModels.DownloadViewModel(settings, new FakeGoogleDriveClient(), transfer, new SongInstallServiceStub(), new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db")));

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
    public async Task DownloadCloudToLocalCommand_WithSelectedFiles_InvokesTransferAndRefreshesWatcher()
    {
        using var temp = new TemporaryDirectoryFixture("download-vm-cloud-local");
        using var settings = CreateSettings(temp.RootPath);
        var transfer = new TransferOrchestratorSpy();
        var sut = new ViewModels.DownloadViewModel(settings, new FakeGoogleDriveClient(), transfer, new SongInstallServiceStub(), new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db")));
        var refreshWatcher = new ResourceWatcherStub(temp.GetPath("downloads"), sut.DownloadFiles);

        try
        {
            sut.DownloadWatcher = refreshWatcher;
            sut.GoogleFiles.Add(CreateWatcherFile("alpha.zip", "drive-file-a", checkedState: true));
            sut.GoogleFiles.Add(CreateWatcherFile("beta.zip", "drive-file-b", checkedState: false));

            await sut.DownloadCloudToLocal.ExecuteAsync(null);

            Assert.Equal(1, transfer.DownloadSelectedCallCount);
            Assert.Single(transfer.LastSelectedCloudFiles!);
            Assert.Equal("drive-file-a", transfer.LastSelectedCloudFiles![0].FilePath);
            Assert.Equal(1, refreshWatcher.LoadItemsCallCount);
        }
        finally
        {
            await sut.DisposeAsync();
        }
    }

    [Fact]
    public async Task SyncCloudToLocalCommand_InvokesTransferForCurrentCloudFiles_AndRefreshesWatcher()
    {
        using var temp = new TemporaryDirectoryFixture("download-vm-sync");
        using var settings = CreateSettings(temp.RootPath);
        var transfer = new TransferOrchestratorSpy();
        var sut = new ViewModels.DownloadViewModel(settings, new FakeGoogleDriveClient(), transfer, new SongInstallServiceStub(), new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db")));
        var refreshWatcher = new ResourceWatcherStub(temp.GetPath("downloads"), sut.DownloadFiles);

        try
        {
            sut.DownloadWatcher = refreshWatcher;
            sut.GoogleFiles.Add(CreateWatcherFile("alpha.zip", "drive-file-a", checkedState: false));
            sut.GoogleFiles.Add(CreateWatcherFile("beta.zip", "drive-file-b", checkedState: true));

            await sut.SyncCloudToLocal.ExecuteAsync(null);

            Assert.Equal(1, transfer.SyncCallCount);
            Assert.NotNull(transfer.LastCurrentCloudFiles);
            Assert.Equal(2, transfer.LastCurrentCloudFiles!.Count);
            Assert.Equal(1, refreshWatcher.LoadItemsCallCount);
        }
        finally
        {
            await sut.DisposeAsync();
        }
    }

    [Fact]
    public async Task CloudCommands_WhenCloudNotLinked_DoNotThrowAndDoNotInvokeTransfers()
    {
        using var temp = new TemporaryDirectoryFixture("download-vm-cloud-unlinked");
        using var settings = CreateSettings(temp.RootPath);
        var transfer = new TransferOrchestratorSpy();
        var sut = new ViewModels.DownloadViewModel(settings, new FakeGoogleDriveClient { ChartHubFolderId = string.Empty }, transfer, new SongInstallServiceStub(), new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db")));

        try
        {
            sut.DownloadFiles.Add(CreateWatcherFile("alpha.zip", temp.GetPath("alpha.zip"), checkedState: true));
            sut.GoogleFiles.Add(CreateWatcherFile("beta.zip", "drive-file-b", checkedState: true));

            await sut.UploadCloud.ExecuteAsync(null);
            await sut.DownloadCloudToLocal.ExecuteAsync(null);
            await sut.SyncCloudToLocal.ExecuteAsync(null);

            Assert.False(sut.IsCloudConnected);
            Assert.True(sut.HasCloudConnectionHint);
            Assert.Equal("Google Drive is not linked. Open Settings and link your Google account.", sut.CloudConnectionHint);
            Assert.Equal(0, transfer.DownloadSelectedCallCount);
            Assert.Equal(0, transfer.SyncCallCount);
        }
        finally
        {
            await sut.DisposeAsync();
        }
    }

    [Fact]
    public async Task HandleCloudAccountStateChangedAsync_OnUnlink_ClearsCloudFilesAndShowsHint()
    {
        using var temp = new TemporaryDirectoryFixture("download-vm-cloud-link-state");
        using var settings = CreateSettings(temp.RootPath);
        var transfer = new TransferOrchestratorSpy();
        var driveClient = new FakeGoogleDriveClient { ChartHubFolderId = "folder-test" };
        var sut = new ViewModels.DownloadViewModel(settings, driveClient, transfer, new SongInstallServiceStub(), new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db")));

        try
        {
            sut.GoogleFiles.Add(CreateWatcherFile("alpha.zip", "drive-file-a", checkedState: true));
            Assert.Single(sut.GoogleFiles);

            driveClient.ChartHubFolderId = string.Empty;
            await sut.HandleCloudAccountStateChangedAsync(isLinked: false);

            Assert.Empty(sut.GoogleFiles);
            Assert.False(sut.IsCloudConnected);
            Assert.True(sut.HasCloudConnectionHint);
            Assert.Equal("Google Drive is not linked. Open Settings and link your Google account.", sut.CloudConnectionHint);
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
        using var settings = CreateSettings(temp.RootPath);
        var transfer = new TransferOrchestratorSpy();
        var sut = new ViewModels.DownloadViewModel(settings, new FakeGoogleDriveClient(), transfer, new SongInstallServiceStub(), new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db")));

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
        using var settings = CreateSettings(temp.RootPath);
        var transfer = new TransferOrchestratorSpy();
        var selectedFilePath = temp.GetPath("song-a.zip");
        await File.WriteAllTextAsync(selectedFilePath, "zip");
        var installStub = new SongInstallServiceStub
        {
            ResultPaths = [Path.Combine(temp.RootPath, "CloneHero", "Songs", "Song A")],
        };
        var sut = new ViewModels.DownloadViewModel(settings, new FakeGoogleDriveClient(), transfer, installStub, new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db")));

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
            Assert.Equal(string.Empty, sut.InstallLogText);
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
        using var settings = CreateSettings(temp.RootPath);
        var transfer = new TransferOrchestratorSpy();
        var selectedFilePath = temp.GetPath("song-cancel.zip");
        await File.WriteAllTextAsync(selectedFilePath, "zip");
        var installStub = new SongInstallServiceStub
        {
            ThrowOnInstall = new OperationCanceledException(),
        };
        var sut = new ViewModels.DownloadViewModel(settings, new FakeGoogleDriveClient(), transfer, installStub, new SongIngestionCatalogService(Path.Combine(temp.RootPath, "library-catalog.db")));

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

    private sealed class TransferOrchestratorSpy : ITransferOrchestrator
    {
        public int DownloadSelectedCallCount { get; private set; }
        public int SyncCallCount { get; private set; }
        public List<WatcherFile>? LastSelectedCloudFiles { get; private set; }
        public List<WatcherFile>? LastCurrentCloudFiles { get; private set; }

        public Task<TransferResult> QueueSongDownloadAsync(ViewSong song, DownloadFile? downloadItem, ObservableCollection<DownloadFile> downloads, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<string>> DownloadSelectedCloudFilesToLocalAsync(IEnumerable<WatcherFile> selectedCloudFiles, CancellationToken cancellationToken = default)
        {
            DownloadSelectedCallCount++;
            LastSelectedCloudFiles = selectedCloudFiles.ToList();
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        public Task<IReadOnlyList<string>> SyncCloudToLocalAdditiveAsync(IEnumerable<WatcherFile> currentCloudFiles, CancellationToken cancellationToken = default)
        {
            SyncCallCount++;
            LastCurrentCloudFiles = currentCloudFiles.ToList();
            return Task.FromResult<IReadOnlyList<string>>([]);
        }
    }

    private sealed class FakeGoogleDriveClient : IGoogleDriveClient
    {
        public string ChartHubFolderId { get; set; } = "folder-test";

        public Task<string> CreateDirectoryAsync(string directoryName) => Task.FromResult("folder-test");
        public Task<string> GetDirectoryIdAsync(string directoryName) => Task.FromResult("folder-test");
        public Task<string> UploadFileAsync(string directoryId, string filePath, string? desiredFileName = null) => Task.FromResult("file-test");
        public Task<string> CopyFileIntoFolderAsync(string sourceFileId, string destinationFolderId, string desiredFileName) => Task.FromResult("copy-test");
        public Task DownloadFolderAsZipAsync(string folderId, string zipFilePath, IProgress<TransferProgressUpdate>? stageProgress = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DownloadFileAsync(string fileId, string saveToPath) => Task.CompletedTask;
        public Task DeleteFileAsync(string fileId) => Task.CompletedTask;
        public Task<IList<Google.Apis.Drive.v3.Data.File>> ListFilesAsync(string directoryId) => Task.FromResult<IList<Google.Apis.Drive.v3.Data.File>>(new List<Google.Apis.Drive.v3.Data.File>());
        public Task MonitorDirectoryAsync(string directoryId, TimeSpan pollingInterval, Action<Google.Apis.Drive.v3.Data.File, string> onFileChanged, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> TryInitializeSilentAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ObservableCollection<WatcherFile>> GetFileDataCollectionAsync(string directoryId) => Task.FromResult(new ObservableCollection<WatcherFile>());
    }

    private sealed class SongInstallServiceStub : ISongInstallService
    {
        public IReadOnlyList<string> ResultPaths { get; set; } = [];
        public Exception? ThrowOnInstall { get; set; }

        public Task<IReadOnlyList<string>> InstallSelectedDownloadsAsync(IEnumerable<string> selectedFilePaths, IProgress<InstallProgressUpdate>? progress = null, CancellationToken cancellationToken = default)
        {
            if (ThrowOnInstall is not null)
                throw ThrowOnInstall;

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
