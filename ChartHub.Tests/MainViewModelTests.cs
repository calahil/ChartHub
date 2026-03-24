using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;

using ChartHub.Models;
using ChartHub.Services;
using ChartHub.Tests.TestInfrastructure;
using ChartHub.Utilities;
using ChartHub.ViewModels;

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
        CloneHeroViewModel cloneHeroViewModel = CreateCloneHeroViewModel(temp.RootPath);
        DownloadViewModel downloadViewModel = CreateDownloadViewModel(downloadWatcher);
        RhythmVerseViewModel rhythmVerseViewModel = CreateUninitialized<ViewModels.RhythmVerseViewModel>();
        EncoreViewModel encoreViewModel = CreateUninitialized<ViewModels.EncoreViewModel>();
        var sharedDownloadQueue = new SharedDownloadQueue();
        SettingsViewModel settingsViewModel = CreateUninitialized<ViewModels.SettingsViewModel>();
        SyncViewModel syncViewModel = CreateUninitialized<ViewModels.SyncViewModel>();

        MainViewModel sut = CreateMainViewModel(
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
        Assert.True(sut.IsSyncTabVisible);
        Assert.True(sut.IsCloneHeroTabVisible);
        Assert.True(sut.IsDownloadTabVisible);
    }

    [Fact]
    [Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
    public void Constructor_InAndroidMode_ShowsSyncTabAndHidesCloneHero()
    {
        using var temp = new TemporaryDirectoryFixture("main-vm-android");
        var downloadWatcher = new ResourceWatcherStub();
        CloneHeroViewModel cloneHeroViewModel = CreateCloneHeroViewModel(temp.RootPath);
        DownloadViewModel downloadViewModel = CreateDownloadViewModel(downloadWatcher);
        RhythmVerseViewModel rhythmVerseViewModel = CreateUninitialized<ViewModels.RhythmVerseViewModel>();
        EncoreViewModel encoreViewModel = CreateUninitialized<ViewModels.EncoreViewModel>();
        var sharedDownloadQueue = new SharedDownloadQueue();
        SettingsViewModel settingsViewModel = CreateUninitialized<ViewModels.SettingsViewModel>();
        SyncViewModel syncViewModel = CreateUninitialized<ViewModels.SyncViewModel>();

        MainViewModel sut = CreateMainViewModel(
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
    [Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
    public void CompletedAndroidDownload_ReloadsLocalWatcher()
    {
        using var temp = new TemporaryDirectoryFixture("main-vm-android-refresh");
        var downloadWatcher = new ResourceWatcherStub();
        CloneHeroViewModel cloneHeroViewModel = CreateCloneHeroViewModel(temp.RootPath);
        DownloadViewModel downloadViewModel = CreateDownloadViewModel(downloadWatcher);
        RhythmVerseViewModel rhythmVerseViewModel = CreateUninitialized<ViewModels.RhythmVerseViewModel>();
        EncoreViewModel encoreViewModel = CreateUninitialized<ViewModels.EncoreViewModel>();
        var sharedDownloadQueue = new SharedDownloadQueue();
        SettingsViewModel settingsViewModel = CreateUninitialized<ViewModels.SettingsViewModel>();
        SyncViewModel syncViewModel = CreateUninitialized<ViewModels.SyncViewModel>();
        var activeDownload = new DownloadFile("song.zip", temp.RootPath, "https://example.test/song.zip", 42);
        sharedDownloadQueue.Downloads.Add(activeDownload);

        _ = CreateMainViewModel(
            rhythmVerseViewModel,
            encoreViewModel,
            sharedDownloadQueue,
            downloadViewModel,
            cloneHeroViewModel,
            syncViewModel,
            settingsViewModel,
            action => action(),
            isAndroid: true);

        activeDownload.Status = "Completed";

        Assert.Equal(1, downloadWatcher.LoadItemsCallCount);
    }

    [Fact]
    [Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.IntegrationLite)]
    public void ObserveBackgroundTask_WhenTaskFaults_LogsFailure()
    {
        lock (LoggerSync)
        {
            using var temp = new TemporaryDirectoryFixture("main-vm-logging");
            string logPath = Path.Combine(temp.RootPath, "charthub.log");

            Logger.Initialize(temp.RootPath);

            try
            {
                MethodInfo? observeMethod = typeof(ViewModels.MainViewModel).GetMethod("ObserveBackgroundTask", BindingFlags.NonPublic | BindingFlags.Static);

                Assert.NotNull(observeMethod);

                observeMethod.Invoke(null, [Task.FromException(new InvalidOperationException("boom-marker")), "Google watcher startup"]);

                bool wroteFailure = SpinWait.SpinUntil(() =>
                {
                    if (!File.Exists(logPath))
                    {
                        return false;
                    }

                    string text = File.ReadAllText(logPath);
                    return text.Contains("Google watcher startup failed", StringComparison.Ordinal)
                        && text.Contains("boom-marker", StringComparison.Ordinal);
                }, TimeSpan.FromSeconds(2));

                Logger.Shutdown();

                Assert.True(wroteFailure, "Expected background task failure to be written to the log.");

                string finalText = File.ReadAllText(logPath);
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
        ConstructorInfo? constructor = typeof(ViewModels.MainViewModel).GetConstructor(
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
        var ingestionCatalog = new SongIngestionCatalogService(Path.Combine(rootPath, "library-catalog.db"));
        return new ViewModels.CloneHeroViewModel(catalog, ingestionCatalog, new NoopDesktopPathOpener(), new LocalFileDeletionService());
    }

    private static ViewModels.DownloadViewModel CreateDownloadViewModel(IResourceWatcher watcher)
    {
        DownloadViewModel viewModel = CreateUninitialized<ViewModels.DownloadViewModel>();
        viewModel.DownloadWatcher = watcher;
        return viewModel;
    }

    private static T CreateUninitialized<T>() where T : class
    {
        return (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
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