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

    public bool IsCompanionMode => _isAndroidMode;
    public bool IsDesktopMode => !_isAndroidMode;

    private RhythmVerseViewModel _rhythmVerseViewModel = null!;
    private EncoreViewModel _encoreViewModel = null!;
    private DownloadViewModel _downloadViewModel = null!;
    private readonly SharedDownloadQueue _sharedDownloadQueue = new();
    private CloneHeroViewModel _cloneHeroViewModel = null!;
    private SettingsViewModel _settingsViewModel = null!;
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
        4 => SettingsViewModel,
        _ => RhythmVerseViewModel,
    };

    public string CurrentMainTabTitle => SelectedMainTabIndex switch
    {
        0 => PageStrings.RhythmVerse,
        1 => PageStrings.Encore,
        2 => PageStrings.Downloads,
        3 when IsCloneHeroTabVisible => PageStrings.CloneHero,
        4 => PageStrings.Settings,
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
    public IRelayCommand GoSettingsCommand { get; }

    public IRelayCommand<DownloadFile?> CancelSharedDownloadCommand { get; }

    public IRelayCommand<DownloadFile?> ClearSharedDownloadCommand { get; }

    public bool IsSettingsTabVisible
    {
        get => _isSettingsTabVisible;
        set
        {
            _isSettingsTabVisible = value;
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
        GoSettingsCommand = new RelayCommand(() => NavigateToTab(4));
        CancelSharedDownloadCommand = new RelayCommand<DownloadFile?>(CancelSharedDownload);
        ClearSharedDownloadCommand = new RelayCommand<DownloadFile?>(ClearSharedDownload);
    }

    public MainViewModel(
        RhythmVerseViewModel rhythmVerseViewModel,
        EncoreViewModel encoreViewModel,
        SharedDownloadQueue sharedDownloadQueue,
        DownloadViewModel downloadViewModel,
        CloneHeroViewModel cloneHeroViewModel,
        SettingsViewModel settingsViewModel)
        : this(
            rhythmVerseViewModel,
            encoreViewModel,
            sharedDownloadQueue,
            downloadViewModel,
            cloneHeroViewModel,
            settingsViewModel,
            action => Dispatcher.UIThread.Post(action),
            OperatingSystem.IsAndroid())
    {
    }

    internal MainViewModel(
        RhythmVerseViewModel rhythmVerseViewModel,
        EncoreViewModel encoreViewModel,
        SharedDownloadQueue sharedDownloadQueue,
        DownloadViewModel downloadViewModel,
        CloneHeroViewModel cloneHeroViewModel,
        SettingsViewModel settingsViewModel,
        Action<Action> postToUi,
        bool isAndroid)
    {
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
        _settingsViewModel = settingsViewModel;
        ShowFiltersPaneCommand = new RelayCommand(ToggleDesktopFiltersPane);
        ToggleAndroidNavPaneCommand = new RelayCommand(ToggleAndroidNavPane);
        ShowAndroidNavListCommand = new RelayCommand(ShowAndroidNavList);
        ShowAndroidFiltersInFlyoutCommand = new RelayCommand(ShowAndroidFiltersInFlyout);
        GoRhythmVerseCommand = new RelayCommand(() => NavigateToTab(0));
        GoEncoreCommand = new RelayCommand(() => NavigateToTab(1));
        GoDownloadsCommand = new RelayCommand(() => NavigateToTab(2));
        GoCloneHeroCommand = new RelayCommand(() => NavigateToTab(3));
        GoSettingsCommand = new RelayCommand(() => NavigateToTab(4));
        CancelSharedDownloadCommand = new RelayCommand<DownloadFile?>(CancelSharedDownload);
        ClearSharedDownloadCommand = new RelayCommand<DownloadFile?>(ClearSharedDownload);

        bool supportsCloneHero = !isAndroid;

        _isCloneHeroTabVisible = supportsCloneHero;
        _isDownloadTabVisible = false;
        _isSettingsTabVisible = true;

        if (supportsCloneHero)
        {
            ObserveBackgroundTask(InitializeCloneHeroAsync(postToUi), "Clone Hero startup reconciliation");
        }

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

        SelectedMainTabIndex = tabIndex;

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
