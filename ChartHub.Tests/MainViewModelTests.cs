using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;

using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using ChartHub.Models;
using ChartHub.Services;
using ChartHub.Tests.TestInfrastructure;
using ChartHub.Utilities;
using ChartHub.ViewModels;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public class MainViewModelTests
{
    [Fact]
    public void Constructor_InDesktopMode_ShowsCloneHeroAndSettingsTabs()
    {
        MainViewModel sut = CreateMainViewModel(isAndroid: false);

        Assert.True(sut.IsDesktopMode);
        Assert.False(sut.IsCompanionMode);
        Assert.True(sut.IsCloneHeroTabVisible);
        Assert.True(sut.IsDesktopEntryTabVisible);
        Assert.True(sut.IsVolumeTabVisible);
        Assert.True(sut.IsSettingsTabVisible);
        Assert.True(sut.IsDownloadTabVisible);
    }

    [Fact]
    public void Constructor_InAndroidMode_ShowsCloneHero()
    {
        MainViewModel sut = CreateMainViewModel(isAndroid: true);

        Assert.True(sut.IsCompanionMode);
        Assert.False(sut.IsDesktopMode);
        Assert.True(sut.IsCloneHeroTabVisible);
        Assert.True(sut.IsDesktopEntryTabVisible);
        Assert.True(sut.IsVolumeTabVisible);
        Assert.True(sut.IsSettingsTabVisible);
        Assert.True(sut.IsDownloadTabVisible);
    }

    [Fact]
    public void GoCloneHeroCommand_OnAndroid_SelectsCloneHeroTab()
    {
        MainViewModel sut = CreateMainViewModel(isAndroid: true);

        sut.GoCloneHeroCommand.Execute(null);

        Assert.Equal(3, sut.SelectedMainTabIndex);
    }

    [Fact]
    public void GoSettingsCommand_SelectsSettingsTab()
    {
        MainViewModel sut = CreateMainViewModel(isAndroid: true);

        sut.GoSettingsCommand.Execute(null);

        Assert.Equal(6, sut.SelectedMainTabIndex);
    }

    [Fact]
    public void GoDesktopEntryCommand_SelectsDesktopEntryTab()
    {
        MainViewModel sut = CreateMainViewModel(isAndroid: true);

        sut.GoDesktopEntryCommand.Execute(null);

        Assert.Equal(4, sut.SelectedMainTabIndex);
    }

    [Fact]
    public void AuthServerBaseUrl_Setter_PersistsToGlobalSettings()
    {
        using AppGlobalSettings settings = CreateSettings("https://initial.example");
        MainViewModel sut = CreateMainViewModel(isAndroid: false, globalSettings: settings);

        sut.AuthServerBaseUrl = "  https://updated.example:5001  ";

        Assert.Equal("https://updated.example:5001", sut.AuthServerBaseUrl);
        Assert.Equal("https://updated.example:5001", settings.ServerApiBaseUrl);
    }

    [Fact]
    public async Task GoDesktopEntryCommand_TriggersRefreshWhenTabOpened()
    {
        using AppGlobalSettings settings = CreateSettings("http://127.0.0.1:5001", "token");
        var desktopEntryClient = new FakeDesktopEntryApiClient
        {
            Entries = [new ChartHubServerDesktopEntryResponse("entry-1", "App", "Running", 12, null)],
        };
        using var desktopEntryViewModel = new DesktopEntryViewModel(settings, desktopEntryClient, uiInvoke: InvokeInline);
        MainViewModel sut = CreateMainViewModel(isAndroid: true, globalSettings: settings, desktopEntryViewModel: desktopEntryViewModel);

        await WaitForConditionAsync(() => desktopEntryClient.ListCallCount > 0, TimeSpan.FromSeconds(2));
        int initialListCallCount = desktopEntryClient.ListCallCount;

        sut.GoDesktopEntryCommand.Execute(null);

        bool refreshed = await WaitForConditionAsync(() => desktopEntryClient.ListCallCount > initialListCallCount, TimeSpan.FromSeconds(2));
        Assert.True(refreshed);
        Assert.Equal(4, sut.SelectedMainTabIndex);
    }

    [Fact]
    public void GoVolumeCommand_SelectsVolumeTab()
    {
        MainViewModel sut = CreateMainViewModel(isAndroid: true);

        sut.GoVolumeCommand.Execute(null);

        Assert.Equal(5, sut.SelectedMainTabIndex);
    }

    [Fact]
    public void AndroidNavigationCommand_ClosesPaneAfterNavigation()
    {
        MainViewModel sut = CreateMainViewModel(isAndroid: true);

        sut.ShowAndroidFiltersInFlyoutCommand.Execute(null);
        sut.GoDownloadsCommand.Execute(null);

        Assert.Equal(2, sut.SelectedMainTabIndex);
        Assert.False(sut.IsAndroidNavPaneOpen);
        Assert.False(sut.IsAndroidFlyoutFiltersMode);
        Assert.True(sut.IsAndroidNavListMode);
    }

    [Fact]
    public void SharedDownloadState_UpdatesHasSharedDownloadsFlags()
    {
        var queue = new SharedDownloadQueue();
        MainViewModel sut = CreateMainViewModel(isAndroid: true, sharedDownloadQueue: queue);

        Assert.False(sut.HasSharedDownloads);
        Assert.True(sut.NoSharedDownloads);

        queue.Downloads.Add(new DownloadFile("song.zip", "/tmp", "https://example.test/song.zip", 42));

        Assert.True(sut.HasSharedDownloads);
        Assert.False(sut.NoSharedDownloads);
    }

    [Fact]
    public void GoVirtualControllerCommand_SelectsControllerTab()
    {
        MainViewModel sut = CreateMainViewModel(isAndroid: true);

        sut.GoVirtualControllerCommand.Execute(null);

        Assert.Equal(7, sut.SelectedMainTabIndex);
    }

    [Fact]
    public void GoVirtualTouchPadCommand_SelectsTouchPadTab()
    {
        MainViewModel sut = CreateMainViewModel(isAndroid: true);

        sut.GoVirtualTouchPadCommand.Execute(null);

        Assert.Equal(8, sut.SelectedMainTabIndex);
    }

    [Fact]
    public void GoVirtualKeyboardCommand_SelectsKeyboardTab()
    {
        MainViewModel sut = CreateMainViewModel(isAndroid: true);

        sut.GoVirtualKeyboardCommand.Execute(null);

        Assert.Equal(9, sut.SelectedMainTabIndex);
    }

    [Fact]
    public void ToggleInputAccordionCommand_TogglesIsInputAccordionExpanded()
    {
        MainViewModel sut = CreateMainViewModel(isAndroid: true);

        Assert.False(sut.IsInputAccordionExpanded);

        sut.ToggleInputAccordionCommand.Execute(null);
        Assert.True(sut.IsInputAccordionExpanded);

        sut.ToggleInputAccordionCommand.Execute(null);
        Assert.False(sut.IsInputAccordionExpanded);
    }

    private static MainViewModel CreateMainViewModel(
        bool isAndroid,
        SharedDownloadQueue? sharedDownloadQueue = null,
        AppGlobalSettings? globalSettings = null,
        DesktopEntryViewModel? desktopEntryViewModel = null)
    {
        ConstructorInfo? constructor = typeof(MainViewModel).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [
                typeof(RhythmVerseViewModel),
                typeof(EncoreViewModel),
                typeof(SharedDownloadQueue),
                typeof(DownloadViewModel),
                typeof(CloneHeroViewModel),
                typeof(DesktopEntryViewModel),
                typeof(VolumeViewModel),
                typeof(SettingsViewModel),
                typeof(VirtualControllerViewModel),
                typeof(VirtualTouchPadViewModel),
                typeof(VirtualKeyboardViewModel),
                typeof(Action<Action>),
                typeof(bool),
                typeof(IStatusBarService),
                typeof(IAuthSessionService),
                typeof(AppGlobalSettings),
            ],
            modifiers: null);

        Assert.NotNull(constructor);

        SharedDownloadQueue queue = sharedDownloadQueue ?? new SharedDownloadQueue();
        AppGlobalSettings settings = globalSettings ?? CreateSettings(string.Empty);

        return (MainViewModel)constructor.Invoke([
            CreateUninitialized<RhythmVerseViewModel>(),
            CreateUninitialized<EncoreViewModel>(),
            queue,
            CreateUninitialized<DownloadViewModel>(),
            CreateUninitialized<CloneHeroViewModel>(),
            desktopEntryViewModel ?? new DesktopEntryViewModel(settings, new FakeDesktopEntryApiClient(), uiInvoke: InvokeInline),
            CreateUninitialized<VolumeViewModel>(),
            CreateUninitialized<SettingsViewModel>(),
            null,
            null,
            null,
            (Action<Action>)(action => action()),
            isAndroid,
            null,
            null,
            settings,
        ]);
    }

    private static AppGlobalSettings CreateSettings(string baseUrl, string token = "")
    {
        var orchestrator = new FakeSettingsOrchestrator(new AppConfigRoot
        {
            Runtime = new RuntimeAppConfig
            {
                ServerApiBaseUrl = baseUrl,
                ServerApiAuthToken = token,
            },
        });

        return new AppGlobalSettings(orchestrator);
    }

    private static Task InvokeInline(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private static async Task<bool> WaitForConditionAsync(Func<bool> predicate, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (predicate())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return predicate();
    }

    private static T CreateUninitialized<T>() where T : class
    {
        return (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
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

    private sealed class FakeDesktopEntryApiClient : IChartHubServerApiClient
    {
        public IReadOnlyList<ChartHubServerDesktopEntryResponse> Entries { get; set; } = [];
        public int ListCallCount { get; private set; }

        public Task<ChartHubServerAuthExchangeResponse> ExchangeGoogleTokenAsync(string baseUrl, string googleIdToken, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ChartHubServerDownloadJobResponse> CreateDownloadJobAsync(string baseUrl, string bearerToken, ChartHubServerCreateDownloadJobRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ChartHubServerDownloadJobResponse>> ListDownloadJobsAsync(string baseUrl, string bearerToken, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<IReadOnlyList<ChartHubServerDownloadJobResponse>> StreamDownloadJobsAsync(string baseUrl, string bearerToken, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ChartHubServerDownloadJobResponse> RequestInstallDownloadJobAsync(string baseUrl, string bearerToken, Guid jobId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task RequestCancelDownloadJobAsync(string baseUrl, string bearerToken, Guid jobId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ChartHubServerJobLogEntry>> GetJobLogsAsync(string baseUrl, string bearerToken, Guid jobId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ChartHubServerDesktopEntryResponse>> ListDesktopEntriesAsync(string baseUrl, string bearerToken, CancellationToken cancellationToken = default)
        {
            ListCallCount++;
            return Task.FromResult(Entries);
        }

        public Task<ChartHubServerDesktopEntryActionResponse> ExecuteDesktopEntryAsync(string baseUrl, string bearerToken, string entryId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ChartHubServerDesktopEntryActionResponse> KillDesktopEntryAsync(string baseUrl, string bearerToken, string entryId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task RefreshDesktopEntryCatalogAsync(string baseUrl, string bearerToken, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public async IAsyncEnumerable<IReadOnlyList<ChartHubServerDesktopEntryResponse>> StreamDesktopEntriesAsync(string baseUrl, string bearerToken, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            yield break;
        }
    }
}
