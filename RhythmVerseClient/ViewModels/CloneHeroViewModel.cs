using CommunityToolkit.Mvvm.Input;
using RhythmVerseClient.Services;
using RhythmVerseClient.Utilities;
using SharpCompress.Archives;
using SharpCompress.Common;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Runtime.CompilerServices;
using System.Windows.Input;

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

        private bool _isAscending = true;
        public ICommand SortCommand { get; }
        public ICommand CheckAllCommand { get; }

        private bool _isAnyChecked;
        public bool IsAnyChecked
        {
            get => _isAnyChecked;
            set
            {
                if (_isAnyChecked != value)
                {
                    _isAnyChecked = value;
                    OnPropertyChanged();
                }
            }
        }
        private bool _isAllChecked;
        public bool IsAllChecked
        {
            get => _isAllChecked;
            set
            {
                if (_isAllChecked != value)
                {
                    _isAllChecked = value;
                    OnPropertyChanged();
                    CheckAllItems(value);
                }
            }
        }

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

        public CloneHeroViewModel(AppGlobalSettings settings)
        {
            globalSettings = settings;
            CloneHeroWatcher = new ResourceWatcher(globalSettings.CloneHeroSongsDir, WatcherType.Directory);
            _dataItems = CloneHeroWatcher.Data;
            SortCommand = new Command<string>(SortData);
            CheckAllCommand = new Command(CheckAllItemsCommand);
            CloneHeroWatcher.LoadItems();
        }

        private void CheckAllItemsCommand()
        {
            IsAllChecked = !IsAllChecked;
        }

        public void SortData(string columnName)
        {
            if (_isAscending)
            {
                DataItems = new ObservableCollection<FileData>(DataItems.OrderBy(x => GetSortablePropertyValue(x, columnName)));
            }
            else
            {
                DataItems = new ObservableCollection<FileData>(DataItems.OrderByDescending(x => GetSortablePropertyValue(x, columnName)));
            }

            _isAscending = !_isAscending;
        }

        private object? GetSortablePropertyValue(FileData item, string propertyName)
        {
            switch (propertyName)
            {
                case nameof(FileData.FileSize):
                    return item.SizeBytes;
                case nameof(FileData.FileType):
                    return item.FileType;
                default:
                    return item.GetType().GetProperty(propertyName)?.GetValue(item, null);
            }
        }

        public void CheckAllItems(bool isChecked)
        {
            foreach (var item in DataItems)
            {
                item.Checked = isChecked;
            }
            OnPropertyChanged(nameof(DataItems)); // Notify the UI to update
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
