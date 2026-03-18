using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ChartHub.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ChartHub.Utilities;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ChartHub.Strings;

namespace ChartHub.ViewModels
{
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
        private SharedDownloadQueue _sharedDownloadQueue = new();
        private CloneHeroViewModel _cloneHeroViewModel = null!;
        private InstallSongViewModel _installSongViewModel = null!;
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

        public InstallSongViewModel InstallSongViewModel
        {
            get => _installSongViewModel;
            set
            {
                _installSongViewModel = value;
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

        private bool _isInstallSongTabVisible;
        public bool IsInstallSongTabVisible
        {
            get => _isInstallSongTabVisible;
            set
            {
                _isInstallSongTabVisible = value;
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
                    return;

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
                    return;

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
                    return;

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
            ShowFiltersPaneCommand = new RelayCommand(() => TogglePane(SidePaneMode.Filters));
            ShowDownloadsPaneCommand = new RelayCommand(() => TogglePane(SidePaneMode.Downloads));
            CancelSharedDownloadCommand = new RelayCommand<DownloadFile?>(CancelSharedDownload);
        }

        public MainViewModel(
            RhythmVerseViewModel rhythmVerseViewModel,
            EncoreViewModel encoreViewModel,
            SharedDownloadQueue sharedDownloadQueue,
            DownloadViewModel downloadViewModel,
            CloneHeroViewModel cloneHeroViewModel,
            InstallSongViewModel installSongViewModel,
            SettingsViewModel settingsViewModel)
            : this(
                rhythmVerseViewModel,
                encoreViewModel,
                sharedDownloadQueue,
                downloadViewModel,
                cloneHeroViewModel,
                installSongViewModel,
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
            InstallSongViewModel installSongViewModel,
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
            _installSongViewModel = installSongViewModel;
            _settingsViewModel = settingsViewModel;
            ShowFiltersPaneCommand = new RelayCommand(() => TogglePane(SidePaneMode.Filters));
            ShowDownloadsPaneCommand = new RelayCommand(() => TogglePane(SidePaneMode.Downloads));
            CancelSharedDownloadCommand = new RelayCommand<DownloadFile?>(CancelSharedDownload);
            WeakReferenceMessenger.Default.Register<NavigateToTabMessage>(this, (_, msg) =>
                postToUi(() => SelectedMainTabIndex = msg.TabIndex));
            _isCloneHeroTabVisible = false;
            _isDownloadTabVisible = false;
            _isInstallSongTabVisible = false;
            _isSettingsTabVisible = true;

            var supportsCloneHero = !isAndroid;
            var supportsDesktopInstallPipeline = !isAndroid;

            if (supportsCloneHero)
            {
                _cloneHeroViewModel.CloneHeroWatcher.LoadItems();
                postToUi(() => IsCloneHeroTabVisible = true);
            }

            if (supportsDesktopInstallPipeline)
            {
                postToUi(() => IsInstallSongTabVisible = true);
            }

            if (!isAndroid)
            {
                _downloadViewModel.DownloadWatcher.LoadItems();
            }

            ObserveBackgroundTask(_downloadViewModel.GoogleWatcher.StartAsync(), "Google watcher startup");
            postToUi(() => IsDownloadTabVisible = true);
        }

        private static void CancelSharedDownload(DownloadFile? item)
        {
            item?.CancelAction?.Invoke();
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
                var ex = t.Exception?.GetBaseException();
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
}
