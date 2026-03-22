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
    public enum SidePaneMode
    {
        Filters,
        Downloads,
    }

    public bool IsCompanionMode => OperatingSystem.IsAndroid();
    public bool IsDesktopMode => !OperatingSystem.IsAndroid();

    private RhythmVerseViewModel _rhythmVerseViewModel = null!;
    private EncoreViewModel _encoreViewModel = null!;
    private DownloadViewModel _downloadViewModel = null!;
    private readonly SharedDownloadQueue _sharedDownloadQueue = new();
    private CloneHeroViewModel _cloneHeroViewModel = null!;
    private SyncViewModel _syncViewModel = null!;
    private SettingsViewModel _settingsViewModel = null!;
    private SidePaneMode _activeSidePaneMode = SidePaneMode.Filters;
    private MainViewPageStrings _pageStrings = new MainViewPageStrings();

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

    public SyncViewModel SyncViewModel
    {
        get => _syncViewModel;
        set
        {
            _syncViewModel = value;
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
    private bool _isSyncTabVisible;
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
            OnPropertyChanged(nameof(IsDownloadModeActive));
        }
    }

    public SidePaneMode ActiveSidePaneMode
    {
        get => _activeSidePaneMode;
        set
        {
            if (_activeSidePaneMode == value)
            {
                return;
            }

            _activeSidePaneMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsFiltersPaneVisible));
            OnPropertyChanged(nameof(IsDownloadsPaneVisible));
            OnPropertyChanged(nameof(ShowRhythmVerseFilters));
            OnPropertyChanged(nameof(ShowEncoreFilters));
            OnPropertyChanged(nameof(ShowFilterFallback));
        }
    }

    public bool IsFiltersPaneVisible => ActiveSidePaneMode == SidePaneMode.Filters;

    public bool IsDownloadsPaneVisible => ActiveSidePaneMode == SidePaneMode.Downloads;

    public bool IsRhythmVerseTabActive => SelectedMainTabIndex == 0;

    public bool IsEncoreTabActive => SelectedMainTabIndex == 1;

    public bool IsSourceFilterFallbackVisible => !IsRhythmVerseTabActive && !IsEncoreTabActive;

    public bool ShowRhythmVerseFilters => IsFiltersPaneVisible && IsRhythmVerseTabActive;

    public bool ShowEncoreFilters => IsFiltersPaneVisible && IsEncoreTabActive;

    public bool ShowFilterFallback => IsFiltersPaneVisible && IsSourceFilterFallbackVisible;

    public bool IsFilterModeActive => IsFilterPaneOpen && IsFiltersPaneVisible;

    public bool IsDownloadModeActive => IsFilterPaneOpen && IsDownloadsPaneVisible;

    public IRelayCommand ShowFiltersPaneCommand { get; }

    public IRelayCommand ShowDownloadsPaneCommand { get; }

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

    public bool IsSyncTabVisible
    {
        get => _isSyncTabVisible;
        set
        {
            _isSyncTabVisible = value;
            OnPropertyChanged();
        }
    }

    public MainViewModel()
    {
        ShowFiltersPaneCommand = new RelayCommand(() => TogglePane(SidePaneMode.Filters));
        ShowDownloadsPaneCommand = new RelayCommand(() => TogglePane(SidePaneMode.Downloads));
        CancelSharedDownloadCommand = new RelayCommand<DownloadFile?>(CancelSharedDownload);
        ClearSharedDownloadCommand = new RelayCommand<DownloadFile?>(ClearSharedDownload);
    }

    public MainViewModel(
        RhythmVerseViewModel rhythmVerseViewModel,
        EncoreViewModel encoreViewModel,
        SharedDownloadQueue sharedDownloadQueue,
        DownloadViewModel downloadViewModel,
        CloneHeroViewModel cloneHeroViewModel,
        SyncViewModel syncViewModel,
        SettingsViewModel settingsViewModel)
        : this(
            rhythmVerseViewModel,
            encoreViewModel,
            sharedDownloadQueue,
            downloadViewModel,
            cloneHeroViewModel,
            syncViewModel,
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
        SyncViewModel syncViewModel,
        SettingsViewModel settingsViewModel,
        Action<Action> postToUi,
        bool isAndroid)
    {
        _rhythmVerseViewModel = rhythmVerseViewModel;
        _encoreViewModel = encoreViewModel;
        _sharedDownloadQueue = sharedDownloadQueue;
        _sharedDownloadQueue.Downloads.CollectionChanged += SharedDownloads_CollectionChanged;
        _downloadViewModel = downloadViewModel;
        _cloneHeroViewModel = cloneHeroViewModel;
        _syncViewModel = syncViewModel;
        _settingsViewModel = settingsViewModel;
        _settingsViewModel.PropertyChanged += SettingsViewModel_PropertyChanged;
        ShowFiltersPaneCommand = new RelayCommand(() => TogglePane(SidePaneMode.Filters));
        ShowDownloadsPaneCommand = new RelayCommand(() => TogglePane(SidePaneMode.Downloads));
        CancelSharedDownloadCommand = new RelayCommand<DownloadFile?>(CancelSharedDownload);
        ClearSharedDownloadCommand = new RelayCommand<DownloadFile?>(ClearSharedDownload);

        bool supportsCloneHero = !isAndroid;

        _isCloneHeroTabVisible = supportsCloneHero;
        _isDownloadTabVisible = false;
        _isSettingsTabVisible = true;
        _isSyncTabVisible = true;

        if (supportsCloneHero)
        {
            ObserveBackgroundTask(InitializeCloneHeroAsync(postToUi), "Clone Hero startup reconciliation");
        }

        if (!isAndroid)
        {
            _downloadViewModel.DownloadWatcher.LoadItems();
        }

        ObserveBackgroundTask(_downloadViewModel.GoogleWatcher.StartAsync(), "Google watcher startup");
        postToUi(() => IsDownloadTabVisible = true);
    }

    private async Task InitializeCloneHeroAsync(Action<Action> postToUi)
    {
        await _cloneHeroViewModel.InitializeAsync();
    }

    private void SettingsViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SettingsViewModel.IsCloudAccountLinked))
        {
            return;
        }

        ObserveBackgroundTask(
            _downloadViewModel.HandleCloudAccountStateChangedAsync(_settingsViewModel.IsCloudAccountLinked),
            "Google watcher refresh after cloud account state change");
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

    private void TogglePane(SidePaneMode mode)
    {
        if (IsFilterPaneOpen && ActiveSidePaneMode == mode)
        {
            IsFilterPaneOpen = false;
            OnPropertyChanged(nameof(IsFilterModeActive));
            OnPropertyChanged(nameof(IsDownloadModeActive));
            return;
        }

        ActiveSidePaneMode = mode;
        IsFilterPaneOpen = true;
        OnPropertyChanged(nameof(IsFilterModeActive));
        OnPropertyChanged(nameof(IsDownloadModeActive));
    }

    private void SharedDownloads_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasSharedDownloads));
        OnPropertyChanged(nameof(NoSharedDownloads));
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
