using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
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
    [Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
    public void AndroidFiltersCommand_EntersFlyoutFiltersMode()
    {
        using var temp = new TemporaryDirectoryFixture("main-vm-android-filters");
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

        sut.ShowAndroidFiltersInFlyoutCommand.Execute(null);

        Assert.True(sut.IsAndroidNavPaneOpen);
        Assert.True(sut.IsAndroidFlyoutFiltersMode);
        Assert.False(sut.IsAndroidNavListMode);
    }

    [Fact]
    [Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
    public void AndroidNavigationCommand_SelectsTab_AndClosesFlyout()
    {
        using var temp = new TemporaryDirectoryFixture("main-vm-android-nav");
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

        sut.ShowAndroidFiltersInFlyoutCommand.Execute(null);
        sut.GoSyncCommand.Execute(null);

        Assert.Equal(4, sut.SelectedMainTabIndex);
        Assert.False(sut.IsAndroidNavPaneOpen);
        Assert.False(sut.IsAndroidFlyoutFiltersMode);
        Assert.True(sut.IsAndroidNavListMode);
    }

    [Fact]
    [Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
    public void GoSyncCommand_TriggersSyncActivationAttempt()
    {
        using var temp = new TemporaryDirectoryFixture("main-vm-sync-activation");
        var downloadWatcher = new ResourceWatcherStub();
        CloneHeroViewModel cloneHeroViewModel = CreateCloneHeroViewModel(temp.RootPath);
        DownloadViewModel downloadViewModel = CreateDownloadViewModel(downloadWatcher);
        RhythmVerseViewModel rhythmVerseViewModel = CreateUninitialized<ViewModels.RhythmVerseViewModel>();
        EncoreViewModel encoreViewModel = CreateUninitialized<ViewModels.EncoreViewModel>();
        var sharedDownloadQueue = new SharedDownloadQueue();
        SettingsViewModel settingsViewModel = CreateUninitialized<ViewModels.SettingsViewModel>();

        int getVersionCalls = 0;
        SyncViewModel syncViewModel = CreateSyncViewModelForActivation(temp.RootPath, () => getVersionCalls++);

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

        sut.GoSyncCommand.Execute(null);

        bool activationAttempted = SpinWait.SpinUntil(() => Volatile.Read(ref getVersionCalls) > 0, TimeSpan.FromSeconds(2));
        Assert.True(activationAttempted);
    }

    [Fact]
    [Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.IntegrationLite)]
    public void AndroidFlyoutSequence_NavToFiltersBackThenNavigate_WorksAsExpected()
    {
        using var temp = new TemporaryDirectoryFixture("main-vm-android-flyout-sequence");
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

        sut.ToggleAndroidNavPaneCommand.Execute(null);
        Assert.True(sut.IsAndroidNavPaneOpen);
        Assert.True(sut.IsAndroidNavListMode);

        sut.ShowAndroidFiltersInFlyoutCommand.Execute(null);
        Assert.True(sut.IsAndroidFlyoutFiltersMode);

        sut.ShowAndroidNavListCommand.Execute(null);
        Assert.True(sut.IsAndroidNavListMode);
        Assert.False(sut.IsAndroidFlyoutFiltersMode);

        sut.GoDownloadsCommand.Execute(null);

        Assert.Equal(2, sut.SelectedMainTabIndex);
        Assert.False(sut.IsAndroidNavPaneOpen);
        Assert.True(sut.IsAndroidNavListMode);
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

    private static SyncViewModel CreateSyncViewModelForActivation(string rootPath, Action onGetVersion)
    {
        AppGlobalSettings settings = CreateSyncSettings(
            rootPath,
            syncToken: "persisted-token",
            savedConnectionsJson: JsonSerializer.Serialize(new[]
            {
                new
                {
                    apiBaseUrl = "http://192.168.1.55:15123",
                    desktopLabel = "Studio Desktop",
                    lastConnectedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                },
            }));

        return new SyncViewModel(
            new StubDesktopSyncApiClient
            {
                GetVersionHandler = (_, _, _) =>
                {
                    onGetVersion();
                    return Task.FromResult(new DesktopSyncVersionResponse("ingestion-sync", "1.0.0", true, true));
                },
                GetIngestionsHandler = (_, _, _, _) => Task.FromResult<IReadOnlyList<IngestionQueueItem>>([]),
            },
            settings,
            isCompanionMode: true);
    }

    private static AppGlobalSettings CreateSyncSettings(string rootPath, string syncToken, string savedConnectionsJson)
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
                SyncApiAuthToken = syncToken,
                SyncApiSavedConnectionsJson = savedConnectionsJson,
            },
        };

        return new AppGlobalSettings(new FakeSettingsOrchestrator(config));
    }

    private sealed class NoopDesktopPathOpener : IDesktopPathOpener
    {
        public Task OpenDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
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

    private sealed class StubDesktopSyncApiClient : IDesktopSyncApiClient
    {
        public Func<string, string, string?, CancellationToken, Task<DesktopSyncPairClaimResponse>>? ClaimPairTokenHandler { get; init; }
        public Func<string, string, CancellationToken, Task<DesktopSyncVersionResponse>>? GetVersionHandler { get; init; }
        public Func<string, string, int, CancellationToken, Task<IReadOnlyList<IngestionQueueItem>>>? GetIngestionsHandler { get; init; }

        public Task<DesktopSyncPairClaimResponse> ClaimPairTokenAsync(string baseUrl, string pairCode, string? deviceLabel = null, CancellationToken cancellationToken = default)
            => ClaimPairTokenHandler?.Invoke(baseUrl, pairCode, deviceLabel, cancellationToken)
                ?? Task.FromResult(new DesktopSyncPairClaimResponse(true, "token", [baseUrl]));

        public Task<DesktopSyncVersionResponse> GetVersionAsync(string baseUrl, string token, CancellationToken cancellationToken = default)
            => GetVersionHandler?.Invoke(baseUrl, token, cancellationToken)
                ?? Task.FromResult(new DesktopSyncVersionResponse("ingestion-sync", "1.0.0", true, true));

        public Task<IReadOnlyList<IngestionQueueItem>> GetIngestionsAsync(string baseUrl, string token, int limit = 100, CancellationToken cancellationToken = default)
            => GetIngestionsHandler?.Invoke(baseUrl, token, limit, cancellationToken)
                ?? Task.FromResult<IReadOnlyList<IngestionQueueItem>>([]);

        public Task<long> UploadIngestionFileAsync(string baseUrl, string token, string localPath, string displayName, LocalIngestionUploadMetadata? metadata = null, CancellationToken cancellationToken = default)
            => Task.FromResult(1L);

        public Task TriggerRetryAsync(string baseUrl, string token, long ingestionId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task TriggerInstallAsync(string baseUrl, string token, long ingestionId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task TriggerOpenFolderAsync(string baseUrl, string token, long ingestionId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
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