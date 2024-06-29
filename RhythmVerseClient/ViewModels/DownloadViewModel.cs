using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using RhythmVerseClient.Services;

namespace RhythmVerseClient.ViewModels
{
    public class DownloadViewModel : INotifyPropertyChanged
    {
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

        private bool _isAscending = true;
        public ICommand SortCommand { get; }
        public ICommand CheckAllCommand { get; }

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
        public DownloadViewModel()
        {
            DataItems = new ObservableCollection<FileData>();
            SortCommand = new Command<string>(SortData);
            CheckAllCommand = new Command(CheckAllItemsCommand);
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

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
