using RhythmVerseClient.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

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

        public DownloadViewModel()
        {
            DataItems = new ObservableCollection<FileData>();
            SortCommand = new Command<string>(SortData);
            CheckAllCommand = new Command(CheckAllItems);
        }

        public void SortData(string columnName)
        {
            if (_isAscending)
            {
                DataItems = new ObservableCollection<FileData>(DataItems.OrderBy(x => GetPropertyValue(x, columnName)));
            }
            else
            {
                DataItems = new ObservableCollection<FileData>(DataItems.OrderByDescending(x => GetPropertyValue(x, columnName)));
            }

            _isAscending = !_isAscending;
        }

        private object GetPropertyValue(object obj, string propertyName)
        {
            return obj.GetType().GetProperty(propertyName).GetValue(obj, null);
        }
        
        private void OnCheckAllChanged(object sender, CheckedChangedEventArgs e)
        {
            var viewModel = BindingContext as DownloadViewModel;
            if (viewModel != null)
            {
                viewModel.CheckAllItems(e.Value);
            }
        }

        private void CheckAllItems()
        {
            bool newValue = !DataItems.All(item => item.Checked);
            foreach (var item in DataItems)
            {
                item.Checked = newValue;
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
