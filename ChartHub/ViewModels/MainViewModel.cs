using Avalonia.Threading;
using ChartHub.Utilities;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ChartHub.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public bool IsCompanionMode => OperatingSystem.IsAndroid();
        public bool IsDesktopMode => !OperatingSystem.IsAndroid();

        private RhythmVerseViewModel _rhythmVerseViewModel = null!;
        private DownloadViewModel _downloadViewModel = null!;
        private CloneHeroViewModel _cloneHeroViewModel = null!;
        private InstallSongViewModel _installSongViewModel = null!;
        private SettingsViewModel _settingsViewModel = null!;

        public RhythmVerseViewModel RhythmVerseViewModel
        {
            get => _rhythmVerseViewModel;
            set
            {
                _rhythmVerseViewModel = value;
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

        private bool _isSettingsTabVisible;
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
        }

        public MainViewModel(
            RhythmVerseViewModel rhythmVerseViewModel,
            DownloadViewModel downloadViewModel,
            CloneHeroViewModel cloneHeroViewModel,
            InstallSongViewModel installSongViewModel,
            SettingsViewModel settingsViewModel)
            : this(
                rhythmVerseViewModel,
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
            DownloadViewModel downloadViewModel,
            CloneHeroViewModel cloneHeroViewModel,
            InstallSongViewModel installSongViewModel,
            SettingsViewModel settingsViewModel,
            Action<Action> postToUi,
            bool isAndroid)
        {
            _rhythmVerseViewModel = rhythmVerseViewModel;
            _downloadViewModel = downloadViewModel;
            _cloneHeroViewModel = cloneHeroViewModel;
            _installSongViewModel = installSongViewModel;
            _settingsViewModel = settingsViewModel;
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
