using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using Avalonia.Threading;

using ChartHub.Services;
using ChartHub.Strings;
using ChartHub.Utilities;

using CommunityToolkit.Mvvm.Input;

namespace ChartHub.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly bool _isAndroidMode;
    private readonly IAuthSessionService? _authSessionService;
    private readonly AppGlobalSettings? _globalSettings;
    private readonly IStatusBarService _statusBarService = new StatusBarService();
    private string _statusBarMessage = string.Empty;

    public bool IsCompanionMode => _isAndroidMode;
    public bool IsDesktopMode => !_isAndroidMode;

    public string StatusBarMessage
    {
        get => _statusBarMessage;
        private set
        {
            _statusBarMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasStatusBarMessage));
        }
    }

    public bool HasStatusBarMessage => !string.IsNullOrEmpty(StatusBarMessage);

    public bool IsAuthenticated => _authSessionService?.CurrentState == AuthSessionState.Authenticated;

    public bool IsAuthenticating => _authSessionService?.CurrentState == AuthSessionState.Authenticating;

    public bool ShowAuthOverlay => !IsAuthenticated;

    public string AuthButtonText => IsAuthenticated ? "Sign Out" : "Sign In";

    public string AuthOverlayMessage => IsAuthenticating
        ? "Authenticating..."
        : "Sign in is required to use ChartHub Server features.";

    public string AuthServerBaseUrl
    {
        get => _authServerBaseUrl;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (string.Equals(_authServerBaseUrl, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _authServerBaseUrl = normalized;
            if (_globalSettings is not null)
            {
                _globalSettings.ServerApiBaseUrl = normalized;
            }

            if (!string.IsNullOrWhiteSpace(_authError))
            {
                _authError = null;
                OnPropertyChanged(nameof(AuthErrorMessage));
            }

            OnPropertyChanged();
        }
    }

    public string? AuthErrorMessage => _authError;

    private string? _authError;
    private string _authServerBaseUrl = string.Empty;

    private RhythmVerseViewModel _rhythmVerseViewModel = null!;
    private EncoreViewModel _encoreViewModel = null!;
    private DownloadViewModel _downloadViewModel = null!;
    private readonly SharedDownloadQueue _sharedDownloadQueue = new();
    private CloneHeroViewModel _cloneHeroViewModel = null!;
    private DesktopEntryViewModel _desktopEntryViewModel = null!;
    private VolumeViewModel _volumeViewModel = null!;
    private SettingsViewModel _settingsViewModel = null!;
    private VirtualControllerViewModel _virtualControllerViewModel = null!;
    private VirtualTouchPadViewModel _virtualTouchPadViewModel = null!;
    private VirtualKeyboardViewModel _virtualKeyboardViewModel = null!;
    private MainViewPageStrings _pageStrings = new MainViewPageStrings();
    private bool _isAndroidNavPaneOpen;
    private bool _isAndroidFlyoutFiltersMode;

    public RhythmVerseViewModel RhythmVerseViewModel
    {
        get => _rhythmVerseViewModel;
        set
        {
            _rhythmVerseViewModel = value;
            OnPropertyChanged();
        }
    }

    public EncoreViewModel EncoreViewModel
    {
        get => _encoreViewModel;
        set
        {
            _encoreViewModel = value;
            OnPropertyChanged();
        }
    }

    public DownloadViewModel DownloadViewModel
    {
        get => _downloadViewModel;
        set
        {
            _downloadViewModel = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<DownloadFile> SharedDownloads => _sharedDownloadQueue.Downloads;

    public bool HasSharedDownloads => SharedDownloads.Count > 0;

    public bool NoSharedDownloads => SharedDownloads.Count == 0;

    public CloneHeroViewModel CloneHeroViewModel
    {
        get => _cloneHeroViewModel;
        set
        {
            _cloneHeroViewModel = value;
            OnPropertyChanged();
        }
    }

    public SettingsViewModel SettingsViewModel
    {
        get => _settingsViewModel;
        set
        {
            _settingsViewModel = value;
            OnPropertyChanged();
        }
    }

    public DesktopEntryViewModel DesktopEntryViewModel
    {
        get => _desktopEntryViewModel;
        set
        {
            _desktopEntryViewModel = value;
            OnPropertyChanged();
        }
    }

    public VolumeViewModel VolumeViewModel
    {
        get => _volumeViewModel;
        set
        {
            _volumeViewModel = value;
            OnPropertyChanged();
        }
    }

    public VirtualControllerViewModel VirtualControllerViewModel
    {
        get => _virtualControllerViewModel;
        set
        {
            _virtualControllerViewModel = value;
            OnPropertyChanged();
        }
    }

    public VirtualTouchPadViewModel VirtualTouchPadViewModel
    {
        get => _virtualTouchPadViewModel;
        set
        {
            _virtualTouchPadViewModel = value;
            OnPropertyChanged();
        }
    }

    public VirtualKeyboardViewModel VirtualKeyboardViewModel
    {
        get => _virtualKeyboardViewModel;
        set
        {
            _virtualKeyboardViewModel = value;
            OnPropertyChanged();
        }
    }

    private bool _isDownloadTabVisible;
    public bool IsDownloadTabVisible
    {
        get => _isDownloadTabVisible;
        set
        {
            _isDownloadTabVisible = value;
            OnPropertyChanged();
        }
    }

    private bool _isCloneHeroTabVisible;
    public bool IsCloneHeroTabVisible
    {
        get => _isCloneHeroTabVisible;
        set
        {
            _isCloneHeroTabVisible = value;
            OnPropertyChanged();
        }
    }

    private bool _isDesktopEntryTabVisible;
    private bool _isVolumeTabVisible;
    public bool IsDesktopEntryTabVisible
    {
        get => _isDesktopEntryTabVisible;
        set
        {
            _isDesktopEntryTabVisible = value;
            OnPropertyChanged();
        }
    }

    public MainViewPageStrings PageStrings
    {
        get => _pageStrings;
        set
        {
            _pageStrings = value;
            OnPropertyChanged();
        }
    }

    private bool _isSettingsTabVisible;
    private int _selectedMainTabIndex;
    private bool _isFilterPaneOpen;
    private bool _isInputAccordionExpanded;

    public int SelectedMainTabIndex
    {
        get => _selectedMainTabIndex;
        set
        {
            if (_selectedMainTabIndex == value)
            {
                return;
            }

            _selectedMainTabIndex = value;
            if (_globalSettings is not null)
            {
                _globalSettings.LastSelectedMainTabIndex = value;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRhythmVerseTabActive));
            OnPropertyChanged(nameof(IsEncoreTabActive));
            OnPropertyChanged(nameof(IsSourceFilterFallbackVisible));
            OnPropertyChanged(nameof(ShowRhythmVerseFilters));
            OnPropertyChanged(nameof(ShowEncoreFilters));
            OnPropertyChanged(nameof(ShowFilterFallback));
            OnPropertyChanged(nameof(SelectedMainContentViewModel));
            OnPropertyChanged(nameof(CurrentMainTabTitle));
        }
    }

    public bool IsFilterPaneOpen
    {
        get => _isFilterPaneOpen;
        set
        {
            if (_isFilterPaneOpen == value)
            {
                return;
            }

            _isFilterPaneOpen = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsFilterModeActive));
            OnPropertyChanged(nameof(ShowRhythmVerseFilters));
            OnPropertyChanged(nameof(ShowEncoreFilters));
            OnPropertyChanged(nameof(ShowFilterFallback));
        }
    }

    public bool IsAndroidNavPaneOpen
    {
        get => _isAndroidNavPaneOpen;
        set
        {
            if (_isAndroidNavPaneOpen == value)
            {
                return;
            }

            _isAndroidNavPaneOpen = value;
            OnPropertyChanged();
        }
    }

    public bool IsAndroidFlyoutFiltersMode
    {
        get => _isAndroidFlyoutFiltersMode;
        set
        {
            if (_isAndroidFlyoutFiltersMode == value)
            {
                return;
            }

            _isAndroidFlyoutFiltersMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAndroidNavListMode));
            OnPropertyChanged(nameof(ShowRhythmVerseFilters));
            OnPropertyChanged(nameof(ShowEncoreFilters));
            OnPropertyChanged(nameof(ShowFilterFallback));
        }
    }

    public bool IsAndroidNavListMode => !IsAndroidFlyoutFiltersMode;

    public bool IsInputAccordionExpanded
    {
        get => _isInputAccordionExpanded;
        set
        {
            if (_isInputAccordionExpanded == value)
            {
                return;
            }

            _isInputAccordionExpanded = value;
            OnPropertyChanged();
        }
    }

    public bool IsRhythmVerseTabActive => SelectedMainTabIndex == 0;

    public bool IsEncoreTabActive => SelectedMainTabIndex == 1;

    public bool IsSourceFilterFallbackVisible => !IsRhythmVerseTabActive && !IsEncoreTabActive;

    public bool ShowRhythmVerseFilters => IsRhythmVerseTabActive && ((IsDesktopMode && IsFilterPaneOpen) || (IsCompanionMode && IsAndroidFlyoutFiltersMode));

    public bool ShowEncoreFilters => IsEncoreTabActive && ((IsDesktopMode && IsFilterPaneOpen) || (IsCompanionMode && IsAndroidFlyoutFiltersMode));

    public bool ShowFilterFallback => IsSourceFilterFallbackVisible && ((IsDesktopMode && IsFilterPaneOpen) || (IsCompanionMode && IsAndroidFlyoutFiltersMode));

    public bool IsFilterModeActive => IsFilterPaneOpen;

    public object SelectedMainContentViewModel => SelectedMainTabIndex switch
    {
        0 => RhythmVerseViewModel,
        1 => EncoreViewModel,
        2 => DownloadViewModel,
        3 when IsCloneHeroTabVisible => CloneHeroViewModel,
        4 => DesktopEntryViewModel,
        5 => VolumeViewModel,
        6 => SettingsViewModel,
        7 => VirtualControllerViewModel,
        8 => VirtualTouchPadViewModel,
        9 => VirtualKeyboardViewModel,
        _ => RhythmVerseViewModel,
    };

    public string CurrentMainTabTitle => SelectedMainTabIndex switch
    {
        0 => PageStrings.RhythmVerse,
        1 => PageStrings.Encore,
        2 => PageStrings.Downloads,
        3 when IsCloneHeroTabVisible => PageStrings.CloneHero,
        4 => PageStrings.DesktopEntry,
        5 => PageStrings.Volume,
        6 => PageStrings.Settings,
        7 => PageStrings.Controller,
        8 => PageStrings.Mouse,
        9 => PageStrings.Keyboard,
        _ => PageStrings.RhythmVerse,
    };

    public IRelayCommand ShowFiltersPaneCommand { get; }
    public IRelayCommand ToggleAndroidNavPaneCommand { get; }
    public IRelayCommand ShowAndroidNavListCommand { get; }
    public IRelayCommand ShowAndroidFiltersInFlyoutCommand { get; }
    public IRelayCommand GoRhythmVerseCommand { get; }
    public IRelayCommand GoEncoreCommand { get; }
    public IRelayCommand GoDownloadsCommand { get; }
    public IRelayCommand GoCloneHeroCommand { get; }
    public IRelayCommand GoDesktopEntryCommand { get; }
    public IRelayCommand GoVolumeCommand { get; }
    public IRelayCommand GoSettingsCommand { get; }
    public IRelayCommand ToggleInputAccordionCommand { get; }
    public IRelayCommand GoVirtualControllerCommand { get; }
    public IRelayCommand GoVirtualTouchPadCommand { get; }
    public IRelayCommand GoVirtualKeyboardCommand { get; }

    public event EventHandler? InputRequested;

    public IRelayCommand<DownloadFile?> CancelSharedDownloadCommand { get; }

    public IRelayCommand<DownloadFile?> ClearSharedDownloadCommand { get; }

    public IAsyncRelayCommand AuthButtonCommand { get; }

    public bool IsSettingsTabVisible
    {
        get => _isSettingsTabVisible;
        set
        {
            _isSettingsTabVisible = value;
            OnPropertyChanged();
        }
    }

    public bool IsVolumeTabVisible
    {
        get => _isVolumeTabVisible;
        set
        {
            _isVolumeTabVisible = value;
            OnPropertyChanged();
        }
    }

    public MainViewModel()
    {
        ShowFiltersPaneCommand = new RelayCommand(ToggleDesktopFiltersPane);
        ToggleAndroidNavPaneCommand = new RelayCommand(ToggleAndroidNavPane);
        ShowAndroidNavListCommand = new RelayCommand(ShowAndroidNavList);
        ShowAndroidFiltersInFlyoutCommand = new RelayCommand(ShowAndroidFiltersInFlyout);
        GoRhythmVerseCommand = new RelayCommand(() => NavigateToTab(0));
        GoEncoreCommand = new RelayCommand(() => NavigateToTab(1));
        GoDownloadsCommand = new RelayCommand(() => NavigateToTab(2));
        GoCloneHeroCommand = new RelayCommand(() => NavigateToTab(3));
        GoDesktopEntryCommand = new RelayCommand(() => NavigateToTab(4));
        GoVolumeCommand = new RelayCommand(() => NavigateToTab(5));
        GoSettingsCommand = new RelayCommand(() => NavigateToTab(6));
        ToggleInputAccordionCommand = new RelayCommand(() => IsInputAccordionExpanded = !IsInputAccordionExpanded);
        GoVirtualControllerCommand = new RelayCommand(() => NavigateToTab(7));
        GoVirtualTouchPadCommand = new RelayCommand(() => NavigateToTab(8));
        GoVirtualKeyboardCommand = new RelayCommand(() => NavigateToTab(9));
        CancelSharedDownloadCommand = new RelayCommand<DownloadFile?>(CancelSharedDownload);
        ClearSharedDownloadCommand = new RelayCommand<DownloadFile?>(ClearSharedDownload);
        AuthButtonCommand = new AsyncRelayCommand(ExecuteAuthButtonAsync);
    }

    public MainViewModel(
        RhythmVerseViewModel rhythmVerseViewModel,
        EncoreViewModel encoreViewModel,
        SharedDownloadQueue sharedDownloadQueue,
        DownloadViewModel downloadViewModel,
        CloneHeroViewModel cloneHeroViewModel,
        DesktopEntryViewModel desktopEntryViewModel,
        VolumeViewModel volumeViewModel,
        SettingsViewModel settingsViewModel,
        IAuthSessionService authSessionService,
        AppGlobalSettings globalSettings)
        : this(
            rhythmVerseViewModel,
            encoreViewModel,
            sharedDownloadQueue,
            downloadViewModel,
            cloneHeroViewModel,
            desktopEntryViewModel,
            volumeViewModel,
            settingsViewModel,
            virtualControllerViewModel: null,
            virtualTouchPadViewModel: null,
            virtualKeyboardViewModel: null,
            action => Dispatcher.UIThread.Post(action),
            OperatingSystem.IsAndroid(),
            App.ServiceProvider?.GetService(typeof(IStatusBarService)) as IStatusBarService,
            authSessionService,
            globalSettings)
    {
    }

    public MainViewModel(
        RhythmVerseViewModel rhythmVerseViewModel,
        EncoreViewModel encoreViewModel,
        SharedDownloadQueue sharedDownloadQueue,
        DownloadViewModel downloadViewModel,
        CloneHeroViewModel cloneHeroViewModel,
        DesktopEntryViewModel desktopEntryViewModel,
        VolumeViewModel volumeViewModel,
        SettingsViewModel settingsViewModel,
        VirtualControllerViewModel? virtualControllerViewModel,
        VirtualTouchPadViewModel? virtualTouchPadViewModel,
        VirtualKeyboardViewModel? virtualKeyboardViewModel,
        IAuthSessionService authSessionService,
        AppGlobalSettings globalSettings)
        : this(
            rhythmVerseViewModel,
            encoreViewModel,
            sharedDownloadQueue,
            downloadViewModel,
            cloneHeroViewModel,
            desktopEntryViewModel,
            volumeViewModel,
            settingsViewModel,
            virtualControllerViewModel,
            virtualTouchPadViewModel,
            virtualKeyboardViewModel,
            action => Dispatcher.UIThread.Post(action),
            OperatingSystem.IsAndroid(),
            App.ServiceProvider?.GetService(typeof(IStatusBarService)) as IStatusBarService,
            authSessionService,
            globalSettings)
    {
    }

    internal MainViewModel(
        RhythmVerseViewModel rhythmVerseViewModel,
        EncoreViewModel encoreViewModel,
        SharedDownloadQueue sharedDownloadQueue,
        DownloadViewModel downloadViewModel,
        CloneHeroViewModel cloneHeroViewModel,
        DesktopEntryViewModel desktopEntryViewModel,
        VolumeViewModel volumeViewModel,
        SettingsViewModel settingsViewModel,
        VirtualControllerViewModel? virtualControllerViewModel,
        VirtualTouchPadViewModel? virtualTouchPadViewModel,
        VirtualKeyboardViewModel? virtualKeyboardViewModel,
        Action<Action> postToUi,
        bool isAndroid,
        IStatusBarService? statusBarService = null,
        IAuthSessionService? authSessionService = null,
        AppGlobalSettings? globalSettings = null)
    {
        _authSessionService = authSessionService;
        _globalSettings = globalSettings;
        _authServerBaseUrl = _globalSettings?.ServerApiBaseUrl?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_authServerBaseUrl))
        {
            string defaultServerBaseUrl = GetFirstRunServerBaseUrlDefault(isAndroid);
            if (!string.IsNullOrWhiteSpace(defaultServerBaseUrl))
            {
                _authServerBaseUrl = defaultServerBaseUrl;
                if (_globalSettings is not null)
                {
                    _globalSettings.ServerApiBaseUrl = defaultServerBaseUrl;
                }
            }
        }
        _rhythmVerseViewModel = rhythmVerseViewModel;
        _encoreViewModel = encoreViewModel;
        _isAndroidMode = isAndroid;
        _sharedDownloadQueue = sharedDownloadQueue;
        _sharedDownloadQueue.Downloads.CollectionChanged += SharedDownloads_CollectionChanged;
        foreach (DownloadFile item in _sharedDownloadQueue.Downloads)
        {
            item.PropertyChanged += SharedDownloadItem_PropertyChanged;
        }
        _downloadViewModel = downloadViewModel;
        _cloneHeroViewModel = cloneHeroViewModel;
        _desktopEntryViewModel = desktopEntryViewModel;
        if (_isAndroidMode)
        {
            _desktopEntryViewModel.AppLaunched += (_, _) => InputRequested?.Invoke(this, EventArgs.Empty);
        }
        _volumeViewModel = volumeViewModel;
        _settingsViewModel = settingsViewModel;
        _virtualControllerViewModel = virtualControllerViewModel ?? new VirtualControllerViewModel();
        _virtualTouchPadViewModel = virtualTouchPadViewModel ?? new VirtualTouchPadViewModel();
        _virtualKeyboardViewModel = virtualKeyboardViewModel ?? new VirtualKeyboardViewModel();

        if (statusBarService is not null)
        {
            _statusBarService = statusBarService;
            _statusBarService.MessageChanged += (_, _) => postToUi(() => StatusBarMessage = _statusBarService.CurrentMessage);
        }
        ShowFiltersPaneCommand = new RelayCommand(ToggleDesktopFiltersPane);
        ToggleAndroidNavPaneCommand = new RelayCommand(ToggleAndroidNavPane);
        ShowAndroidNavListCommand = new RelayCommand(ShowAndroidNavList);
        ShowAndroidFiltersInFlyoutCommand = new RelayCommand(ShowAndroidFiltersInFlyout);
        GoRhythmVerseCommand = new RelayCommand(() => NavigateToTab(0));
        GoEncoreCommand = new RelayCommand(() => NavigateToTab(1));
        GoDownloadsCommand = new RelayCommand(() => NavigateToTab(2));
        GoCloneHeroCommand = new RelayCommand(() => NavigateToTab(3));
        GoDesktopEntryCommand = new RelayCommand(() => NavigateToTab(4));
        GoVolumeCommand = new RelayCommand(() => NavigateToTab(5));
        GoSettingsCommand = new RelayCommand(() => NavigateToTab(6));
        ToggleInputAccordionCommand = new RelayCommand(() => IsInputAccordionExpanded = !IsInputAccordionExpanded);
        GoVirtualControllerCommand = new RelayCommand(() => NavigateToTab(7));
        GoVirtualTouchPadCommand = new RelayCommand(() => NavigateToTab(8));
        GoVirtualKeyboardCommand = new RelayCommand(() => NavigateToTab(9));
        CancelSharedDownloadCommand = new RelayCommand<DownloadFile?>(CancelSharedDownload);
        ClearSharedDownloadCommand = new RelayCommand<DownloadFile?>(ClearSharedDownload);
        AuthButtonCommand = new AsyncRelayCommand(ExecuteAuthButtonAsync);

        _isCloneHeroTabVisible = true;
        _isDesktopEntryTabVisible = true;
        _isVolumeTabVisible = true;
        _isDownloadTabVisible = false;
        _isSettingsTabVisible = true;

        // Restore last tab on subsequent launches (default remains RhythmVerse = 0).
        int restoredTab = _globalSettings?.LastSelectedMainTabIndex ?? 0;
        _selectedMainTabIndex = Math.Clamp(restoredTab, 0, 9);

        if (_authSessionService is not null)
        {
            _authSessionService.SessionStateChanged += OnAuthSessionStateChanged;
        }

        ObserveBackgroundTask(InitializeCloneHeroAsync(postToUi), "Clone Hero startup reconciliation");

        postToUi(() => IsDownloadTabVisible = true);
    }

    private async Task InitializeCloneHeroAsync(Action<Action> postToUi)
    {
        await _cloneHeroViewModel.InitializeAsync();
    }

    private static void CancelSharedDownload(DownloadFile? item)
    {
        item?.CancelAction?.Invoke();
    }

    private void ClearSharedDownload(DownloadFile? item)
    {
        if (item is null)
        {
            return;
        }

        SharedDownloads.Remove(item);
    }

    private void ToggleDesktopFiltersPane()
    {
        if (!IsDesktopMode)
        {
            return;
        }

        IsFilterPaneOpen = !IsFilterPaneOpen;
    }

    private void ToggleAndroidNavPane()
    {
        if (!IsCompanionMode)
        {
            return;
        }

        IsAndroidNavPaneOpen = !IsAndroidNavPaneOpen;
    }

    private void ShowAndroidNavList()
    {
        if (!IsCompanionMode)
        {
            return;
        }

        IsAndroidFlyoutFiltersMode = false;
        IsAndroidNavPaneOpen = true;
    }

    private void ShowAndroidFiltersInFlyout()
    {
        if (!IsCompanionMode)
        {
            return;
        }

        IsAndroidFlyoutFiltersMode = true;
        IsAndroidNavPaneOpen = true;
    }

    private void NavigateToTab(int tabIndex)
    {
        if (tabIndex == 3 && !IsCloneHeroTabVisible)
        {
            return;
        }

        if (tabIndex == 4 && !IsDesktopEntryTabVisible)
        {
            return;
        }

        if (tabIndex == 5 && !IsVolumeTabVisible)
        {
            return;
        }

        // Input sub-pages (7, 8, 9) are Android-only.
        if ((tabIndex == 7 || tabIndex == 8 || tabIndex == 9) && !IsCompanionMode)
        {
            return;
        }

        SelectedMainTabIndex = tabIndex;

        if (tabIndex == 4)
        {
            ObserveBackgroundTask(_desktopEntryViewModel.RefreshCommand.ExecuteAsync(null), "DesktopEntry refresh on tab open");
        }

        if (IsCompanionMode)
        {
            IsAndroidFlyoutFiltersMode = false;
            IsAndroidNavPaneOpen = false;
        }
    }

    private void SharedDownloads_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (DownloadFile item in e.NewItems)
            {
                item.PropertyChanged += SharedDownloadItem_PropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (DownloadFile item in e.OldItems)
            {
                item.PropertyChanged -= SharedDownloadItem_PropertyChanged;
            }
        }

        OnPropertyChanged(nameof(HasSharedDownloads));
        OnPropertyChanged(nameof(NoSharedDownloads));
    }

    private void SharedDownloadItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!IsCompanionMode
            || sender is not DownloadFile item
            || e.PropertyName != nameof(DownloadFile.Status)
            || (!string.Equals(item.Status, "Installed", StringComparison.Ordinal)
                && !string.Equals(item.Status, "Completed", StringComparison.Ordinal)))
        {
            return;
        }

    }

    private async Task ExecuteAuthButtonAsync()
    {
        if (_authSessionService is null)
        {
            return;
        }

        _authError = null;
        OnPropertyChanged(nameof(AuthErrorMessage));

        try
        {
            if (_authSessionService.CurrentState == AuthSessionState.Authenticated)
            {
                await _authSessionService.SignOutAsync().ConfigureAwait(false);
                return;
            }

            if (string.IsNullOrWhiteSpace(AuthServerBaseUrl))
            {
                throw new InvalidOperationException(
                    "Set the ChartHub Server URL before signing in.");
            }

            if (_globalSettings is not null)
            {
                _globalSettings.ServerApiBaseUrl = AuthServerBaseUrl;
            }

            await _authSessionService.SignInAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _authError = ex.Message;
            OnPropertyChanged(nameof(AuthErrorMessage));
        }
    }

    private void OnAuthSessionStateChanged(object? sender, EventArgs e)
    {
        _authError = null;
        OnPropertyChanged(nameof(IsAuthenticated));
        OnPropertyChanged(nameof(IsAuthenticating));
        OnPropertyChanged(nameof(ShowAuthOverlay));
        OnPropertyChanged(nameof(AuthButtonText));
        OnPropertyChanged(nameof(AuthOverlayMessage));
        OnPropertyChanged(nameof(AuthErrorMessage));
    }

    private static string GetFirstRunServerBaseUrlDefault(bool isAndroid)
    {
        if (!isAndroid)
        {
            return string.Empty;
        }

#if ANDROID
        if (IsLikelyAndroidEmulator())
        {
            return "https://10.0.2.2:5001";
        }
#endif

        return string.Empty;
    }

#if ANDROID
    private static bool IsLikelyAndroidEmulator()
    {
        string fingerprint = global::Android.OS.Build.Fingerprint ?? string.Empty;
        string model = global::Android.OS.Build.Model ?? string.Empty;
        string brand = global::Android.OS.Build.Brand ?? string.Empty;
        string device = global::Android.OS.Build.Device ?? string.Empty;
        string product = global::Android.OS.Build.Product ?? string.Empty;

        return
            fingerprint.Contains("generic", StringComparison.OrdinalIgnoreCase)
            || fingerprint.Contains("emulator", StringComparison.OrdinalIgnoreCase)
            || model.Contains("Emulator", StringComparison.OrdinalIgnoreCase)
            || model.Contains("Android SDK built for", StringComparison.OrdinalIgnoreCase)
            || (brand.Contains("generic", StringComparison.OrdinalIgnoreCase)
                && device.Contains("generic", StringComparison.OrdinalIgnoreCase))
            || product.Contains("sdk", StringComparison.OrdinalIgnoreCase)
            || product.Contains("emulator", StringComparison.OrdinalIgnoreCase)
            || product.Contains("simulator", StringComparison.OrdinalIgnoreCase);
    }
#endif

    private static void ObserveBackgroundTask(Task task, string context)
    {
        _ = task.ContinueWith(t =>
        {
            Exception? ex = t.Exception?.GetBaseException();
            if (ex is not null)
            {
                Logger.LogError("App", $"{context} failed", ex);
            }
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
