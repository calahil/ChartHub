using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RhythmVerseClient.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private RhythmVerseModel _rhythmVerseViewModel;
        private DownloadViewModel _downloadViewModel;
        private CloneHeroViewModel _cloneHeroViewModel;
        private InstallSongViewModel _installSongViewModel;

        public RhythmVerseModel RhythmVerseViewModel
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

        public MainViewModel()
        {
        }

        public MainViewModel(RhythmVerseModel rhythmVerseViewModel, DownloadViewModel downloadViewModel, CloneHeroViewModel cloneHeroViewModel, InstallSongViewModel installSongViewModel)
        {
            _rhythmVerseViewModel = rhythmVerseViewModel;
            _downloadViewModel = downloadViewModel;
            _cloneHeroViewModel = cloneHeroViewModel;
            _installSongViewModel = installSongViewModel;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
