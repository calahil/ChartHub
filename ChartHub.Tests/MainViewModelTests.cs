using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using ChartHub.Models;
using ChartHub.Services;
using ChartHub.Tests.TestInfrastructure;
using ChartHub.Utilities;

namespace ChartHub.Tests;

public class MainViewModelTests
{
    private static readonly object LoggerSync = new();

    [Fact]
    [Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
    public void Constructor_InDesktopMode_LoadsWatchers_AndShowsDesktopTabs()
    {
        using var temp = new TemporaryDirectoryFixture("main-vm-clonehero");
        var downloadWatcher = new ResourceWatcherStub();
        var cloneHeroViewModel = CreateCloneHeroViewModel(temp.RootPath);
        var downloadViewModel = CreateDownloadViewModel(downloadWatcher, new FakeGoogleDriveClient(string.Empty));
        var rhythmVerseViewModel = CreateUninitialized<ViewModels.RhythmVerseViewModel>();
        var encoreViewModel = CreateUninitialized<ViewModels.EncoreViewModel>();
        var sharedDownloadQueue = new SharedDownloadQueue();
        var settingsViewModel = CreateUninitialized<ViewModels.SettingsViewModel>();
        var syncViewModel = CreateUninitialized<ViewModels.SyncViewModel>();

        var sut = CreateMainViewModel(
            rhythmVerseViewModel,
            encoreViewModel,
            sharedDownloadQueue,
            downloadViewModel,
            cloneHeroViewModel,
            syncViewModel,
            settingsViewModel,
            action => action(),
            isAndroid: false);

        Assert.Same(rhythmVerseViewModel, sut.RhythmVerseViewModel);
        Assert.Same(encoreViewModel, sut.EncoreViewModel);
        Assert.Same(sharedDownloadQueue.Downloads, sut.SharedDownloads);
        Assert.Same(downloadViewModel, sut.DownloadViewModel);
        Assert.Same(cloneHeroViewModel, sut.CloneHeroViewModel);
        Assert.Same(syncViewModel, sut.SyncViewModel);
        Assert.Same(settingsViewModel, sut.SettingsViewModel);
        Assert.Equal(1, downloadWatcher.LoadItemsCallCount);
        Assert.True(sut.IsSettingsTabVisible);
        Assert.False(sut.IsSyncTabVisible);
        Assert.True(sut.IsCloneHeroTabVisible);
        Assert.True(sut.IsDownloadTabVisible);
    }

    [Fact]
    [Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
    public void Constructor_InAndroidMode_ShowsSyncTabAndHidesCloneHero()
    {
        using var temp = new TemporaryDirectoryFixture("main-vm-android");
        var downloadWatcher = new ResourceWatcherStub();
        var cloneHeroViewModel = CreateCloneHeroViewModel(temp.RootPath);
        var downloadViewModel = CreateDownloadViewModel(downloadWatcher, new FakeGoogleDriveClient(string.Empty));
        var rhythmVerseViewModel = CreateUninitialized<ViewModels.RhythmVerseViewModel>();
        var encoreViewModel = CreateUninitialized<ViewModels.EncoreViewModel>();
        var sharedDownloadQueue = new SharedDownloadQueue();
        var settingsViewModel = CreateUninitialized<ViewModels.SettingsViewModel>();
        var syncViewModel = CreateUninitialized<ViewModels.SyncViewModel>();

        var sut = CreateMainViewModel(
            rhythmVerseViewModel,
            encoreViewModel,
            sharedDownloadQueue,
            downloadViewModel,
            cloneHeroViewModel,
            syncViewModel,
            settingsViewModel,
            action => action(),
            isAndroid: true);

        Assert.True(sut.IsSyncTabVisible);
        Assert.False(sut.IsCloneHeroTabVisible);
        Assert.True(sut.IsDownloadTabVisible);
        Assert.Equal(0, downloadWatcher.LoadItemsCallCount);
    }

    [Fact]
    [Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.IntegrationLite)]
    public void ObserveBackgroundTask_WhenTaskFaults_LogsFailure()
    {
        lock (LoggerSync)
        {
            using var temp = new TemporaryDirectoryFixture("main-vm-logging");
            var logPath = Path.Combine(temp.RootPath, "charthub.log");

            Logger.Initialize(temp.RootPath);

            try
            {
                var observeMethod = typeof(ViewModels.MainViewModel).GetMethod("ObserveBackgroundTask", BindingFlags.NonPublic | BindingFlags.Static);

                Assert.NotNull(observeMethod);

                observeMethod.Invoke(null, [Task.FromException(new InvalidOperationException("boom-marker")), "Google watcher startup"]);

                var wroteFailure = SpinWait.SpinUntil(() =>
                {
                    if (!File.Exists(logPath))
                    {
                        return false;
                    }

                    var text = File.ReadAllText(logPath);
                    return text.Contains("Google watcher startup failed", StringComparison.Ordinal)
                        && text.Contains("boom-marker", StringComparison.Ordinal);
                }, TimeSpan.FromSeconds(2));

                Logger.Shutdown();

                Assert.True(wroteFailure, "Expected background task failure to be written to the log.");

                var finalText = File.ReadAllText(logPath);
                Assert.Contains("Google watcher startup failed", finalText, StringComparison.Ordinal);
                Assert.Contains("exceptionType=System.InvalidOperationException", finalText, StringComparison.Ordinal);
                Assert.Contains("boom-marker", finalText, StringComparison.Ordinal);
            }
            finally
            {
                Logger.Shutdown();
            }
        }
    }

    private static ViewModels.MainViewModel CreateMainViewModel(
        ViewModels.RhythmVerseViewModel rhythmVerseViewModel,
        ViewModels.EncoreViewModel encoreViewModel,
        SharedDownloadQueue sharedDownloadQueue,
        ViewModels.DownloadViewModel downloadViewModel,
        ViewModels.CloneHeroViewModel cloneHeroViewModel,
        ViewModels.SyncViewModel syncViewModel,
        ViewModels.SettingsViewModel settingsViewModel,
        Action<Action> postToUi,
        bool isAndroid)
    {
        var constructor = typeof(ViewModels.MainViewModel).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [
                typeof(ViewModels.RhythmVerseViewModel),
                typeof(ViewModels.EncoreViewModel),
                typeof(SharedDownloadQueue),
                typeof(ViewModels.DownloadViewModel),
                typeof(ViewModels.CloneHeroViewModel),
                typeof(ViewModels.SyncViewModel),
                typeof(ViewModels.SettingsViewModel),
                typeof(Action<Action>),
                typeof(bool),
            ],
            modifiers: null);

        Assert.NotNull(constructor);

        return (ViewModels.MainViewModel)constructor.Invoke([
            rhythmVerseViewModel,
            encoreViewModel,
            sharedDownloadQueue,
            downloadViewModel,
            cloneHeroViewModel,
            syncViewModel,
            settingsViewModel,
            postToUi,
            isAndroid,
        ]);
    }

    private static ViewModels.CloneHeroViewModel CreateCloneHeroViewModel(string rootPath)
    {
        var catalog = new LibraryCatalogService(Path.Combine(rootPath, "library-catalog.db"));
        return new ViewModels.CloneHeroViewModel(catalog, new NoopDesktopPathOpener());
    }

    private static ViewModels.DownloadViewModel CreateDownloadViewModel(IResourceWatcher watcher, IGoogleDriveClient driveClient)
    {
        var viewModel = CreateUninitialized<ViewModels.DownloadViewModel>();
        viewModel.DownloadWatcher = watcher;
        viewModel.GoogleWatcher = new GoogleDriveWatcher(driveClient);
        return viewModel;
    }

    private static T CreateUninitialized<T>() where T : class
    {
        return (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    }

    private sealed class FakeGoogleDriveClient(string chartHubFolderId) : IGoogleDriveClient
    {
        public string ChartHubFolderId { get; } = chartHubFolderId;

        public Task<string> CreateDirectoryAsync(string directoryName) => Task.FromResult("folder-test");
        public Task<string> GetDirectoryIdAsync(string directoryName) => Task.FromResult("folder-test");
        public Task<string> UploadFileAsync(string directoryId, string filePath, string? desiredFileName = null) => Task.FromResult("file-test");
        public Task<string> CopyFileIntoFolderAsync(string sourceFileId, string destinationFolderId, string desiredFileName) => Task.FromResult("copy-test");
        public Task DownloadFolderAsZipAsync(string folderId, string zipFilePath, IProgress<Services.Transfers.TransferProgressUpdate>? stageProgress = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DownloadFileAsync(string fileId, string saveToPath) => Task.CompletedTask;
        public Task DeleteFileAsync(string fileId) => Task.CompletedTask;
        public Task<IList<Google.Apis.Drive.v3.Data.File>> ListFilesAsync(string directoryId) => Task.FromResult<IList<Google.Apis.Drive.v3.Data.File>>([]);
        public Task MonitorDirectoryAsync(string directoryId, TimeSpan pollingInterval, Action<Google.Apis.Drive.v3.Data.File, string> onFileChanged, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> TryInitializeSilentAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ObservableCollection<WatcherFile>> GetFileDataCollectionAsync(string directoryId) => Task.FromResult(new ObservableCollection<WatcherFile>());
    }

    private sealed class NoopDesktopPathOpener : IDesktopPathOpener
    {
        public Task OpenDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class ResourceWatcherStub : IResourceWatcher
    {
        public int LoadItemsCallCount { get; private set; }

        public string DirectoryPath => string.Empty;
        public ObservableCollection<WatcherFile> Data { get; set; } = [];
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
}