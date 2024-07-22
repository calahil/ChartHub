using RhythmVerseClient.Services;
using RhythmVerseClient.Strings;
using RhythmVerseClient.Utilities;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RhythmVerseClient.ViewModels
{
    public class CloneHeroViewModel : INotifyPropertyChanged
    {
        private readonly AppGlobalSettings globalSettings;

        private ObservableCollection<FileData> _dataItems;
        public ObservableCollection<FileData> DataItems
        {
            get => _dataItems;
            set
            {
                _dataItems = value;
                OnPropertyChanged();
            }
        }

        public IResourceWatcher CloneHeroWatcher { get; set; }

        private FileData? _selectedFile;
        public FileData? SelectedFile
        {
            get => _selectedFile;
            set
            {
                _selectedFile = value;
                OnPropertyChanged();
            }
        }

        public CloneHeroPageStrings PageStrings { get; }

        public CloneHeroViewModel(AppGlobalSettings settings)
        {
            globalSettings = settings;
            CloneHeroWatcher = new ResourceWatcher(globalSettings.CloneHeroSongsDir, WatcherType.Directory);
            _dataItems = CloneHeroWatcher.Data;
            PageStrings = new CloneHeroPageStrings();
        }

        public bool AnyItemChecked()
        {
            return DataItems.Any(item => item.Checked);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
