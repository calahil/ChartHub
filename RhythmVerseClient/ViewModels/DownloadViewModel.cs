using CommunityToolkit.Mvvm.Input;
using RhythmVerseClient.Pages;
using RhythmVerseClient.Services;
using RhythmVerseClient.Strings;
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
    public class DownloadViewModel : INotifyPropertyChanged
    {
        private readonly AppGlobalSettings globalSettings;

        public IResourceWatcher DownloadWatcher { get; set; }

        private bool _isAscending = true;
        public ICommand SortCommand { get; }
        public ICommand CheckAllCommand { get; }
        public IAsyncRelayCommand InstallSongs { get; }

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
        public FileData SelectedFile
        {
            get => _selectedFile;
            set
            {
                _selectedFile = value;
                OnPropertyChanged();
            }
        }

        public DownloadPageStrings PageStrings { get; set; }

        public DownloadViewModel(AppGlobalSettings settings)
        {
            globalSettings = settings;
            DownloadWatcher = new ResourceWatcher(globalSettings.DownloadDir, WatcherType.File);
            SortCommand = new Command<string>(SortData);
            CheckAllCommand = new Command(CheckAllItemsCommand);
            InstallSongs = new AsyncRelayCommand(InstallSongsCommand);
            //DownloadWatcher.LoadItems();
            PageStrings = new DownloadPageStrings();
        }

        private void CheckAllItemsCommand()
        {
            IsAllChecked = !IsAllChecked;
        }

        private async Task InstallSongsCommand()
        {
            List<string> items = new List<string>();

            foreach (FileData file in DownloadWatcher.Data)
            {
                if (file.Checked)
                {
                    items.Add(file.FilePath);
                }

                // attempt to throttle the system events firing
                await Task.Delay(100);
            }
            foreach (string file in items)
            {
                var displayName = Path.GetFileName(file);
                var newFilePath = Toolbox.ConstructPath(globalSettings.PhaseshiftDir, displayName);

                File.Move(file, newFilePath);

                await Task.Delay(100);
            }

            
            // TODO figure out why the resourcewatchers dont see the change

            var mainPage = Application.Current?.MainPage as MainPage;
            mainPage?.FocusOnTab(2);

        }

        public void SortData(string columnName)
        {
            if (_isAscending)
            {
                DownloadWatcher.Data = new ObservableCollection<FileData>(DownloadWatcher.Data.OrderBy(x => GetSortablePropertyValue(x, columnName)));
            }
            else
            {
                DownloadWatcher.Data = new ObservableCollection<FileData>(DownloadWatcher.Data.OrderByDescending(x => GetSortablePropertyValue(x, columnName)));
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
            foreach (var item in DownloadWatcher.Data)
            {
                item.Checked = isChecked;
            }
            OnPropertyChanged(nameof(DownloadWatcher.Data)); // Notify the UI to update
        }

        public bool AnyItemChecked()
        {
            return DownloadWatcher.Data.Any(item => item.Checked);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }



        //public async Task ProcessZipsAsync(CancellationToken cancellationToken)
        //{
        //    try
        //    {
        //        var files = Directory.EnumerateFiles(globalSettings.PhaseshiftDir);
        //        foreach (var file in files)
        //        {
        //            cancellationToken.ThrowIfCancellationRequested();
        //            var extension = Path.GetExtension(file).ToLower();

        //            if (extension == ".zip" || extension == ".rar" || extension == ".7z")
        //            {
        //                using var archive = Toolbox.OpenArchive(file);
        //                foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
        //                {
        //                    entry.WriteToDirectory(globalSettings.PhaseshiftMusicDir, new ExtractionOptions
        //                    {
        //                        ExtractFullPath = true,
        //                        Overwrite = true
        //                    });
        //                }
        //            }
        //        }
        //        await Task.Delay(500, cancellationToken);
        //    }
        //    catch (OperationCanceledException)
        //    {
        //        await Shell.Current.DisplayAlert("Info", "Operation was cancelled.", "OK");
        //    }
        //    catch (Exception ex)
        //    {
        //        await Shell.Current.DisplayAlert("Error", $"An error occurred: {ex.Message}", "OK");
        //    }
        //}
    }
}
