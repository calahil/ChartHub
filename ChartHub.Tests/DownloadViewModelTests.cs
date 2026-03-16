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
        var sut = new ViewModels.DownloadViewModel(settings, new FakeGoogleDriveClient(), transfer);

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
        var sut = new ViewModels.DownloadViewModel(settings, new FakeGoogleDriveClient(), transfer);
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
        var sut = new ViewModels.DownloadViewModel(settings, new FakeGoogleDriveClient(), transfer);
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
        public string ChartHubFolderId => "folder-test";

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
