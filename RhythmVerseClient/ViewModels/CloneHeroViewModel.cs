using RhythmVerseClient.Models;
using RhythmVerseClient.Services;
using RhythmVerseClient.Strings;
using RhythmVerseClient.Utilities;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RhythmVerseClient.ViewModels
{
    public class CloneHeroViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly AppGlobalSettings globalSettings;

        private ObservableCollection<WatcherFile> _dataItems;
        public ObservableCollection<WatcherFile> DataItems
        {
            get => _dataItems;
            set
            {
                _dataItems = value;
                OnPropertyChanged();
            }
        }

        public IResourceWatcher CloneHeroWatcher { get; set; }

        private WatcherFile? _selectedFile;
        public WatcherFile? SelectedFile
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
            CloneHeroWatcher = OperatingSystem.IsAndroid()
                ? new SnapshotResourceWatcher(globalSettings.CloneHeroSongsDir, WatcherType.Directory)
                : new ResourceWatcher(globalSettings.CloneHeroSongsDir, WatcherType.Directory);
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

        public void Dispose()
        {
            if (CloneHeroWatcher is IDisposable disposableWatcher)
                disposableWatcher.Dispose();
        }
    }
}
