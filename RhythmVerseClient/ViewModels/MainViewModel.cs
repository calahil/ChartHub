using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RhythmVerseClient.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private RhythmVerseViewModel _rhythmVerseViewModel;
        private DownloadViewModel _downloadViewModel;
        private CloneHeroViewModel _cloneHeroViewModel;
        private InstallSongViewModel _installSongViewModel;

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

        public MainViewModel()
        {
        }

        public MainViewModel(RhythmVerseViewModel rhythmVerseViewModel, DownloadViewModel downloadViewModel, CloneHeroViewModel cloneHeroViewModel, InstallSongViewModel installSongViewModel)
        {
            _rhythmVerseViewModel = rhythmVerseViewModel;
            _downloadViewModel = downloadViewModel;
            _cloneHeroViewModel = cloneHeroViewModel;
            _installSongViewModel = installSongViewModel;
            _isCloneHeroTabVisible = false;
            _isDownloadTabVisible = false;

            _ = Task.Run(() => {
                _cloneHeroViewModel.CloneHeroWatcher.LoadItems();
                IsCloneHeroTabVisible = true;
                });

            _ = Task.Run(() => {
                _downloadViewModel.DownloadWatcher.LoadItems();
                IsDownloadTabVisible = true;
                });
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
