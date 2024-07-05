using CommunityToolkit.Mvvm.Input;
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
       
        private ObservableCollection<FileData>? _dataItems;
        public ObservableCollection<FileData> DataItems
        {
            get => _dataItems;
            set
            {
                _dataItems = value;
                OnPropertyChanged();
            }
        }

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

        private IKeystrokeSender _keystrokeSender;

        public DownloadPageStrings PageStrings { get; set; }

        public DownloadViewModel(AppGlobalSettings settings, IKeystrokeSender keystrokeSender)
        {
            globalSettings = settings;
            DownloadWatcher = new ResourceWatcher(globalSettings.DownloadDir, WatcherType.File);
            DataItems = DownloadWatcher.Data;
            SortCommand = new Command<string>(SortData);
            CheckAllCommand = new Command(CheckAllItemsCommand);
            InstallSongs = new AsyncRelayCommand(InstallSongsCommand);
            DownloadWatcher.LoadItems();
            _keystrokeSender = keystrokeSender;
            PageStrings = new DownloadPageStrings();
        }

        private void CheckAllItemsCommand()
        {
            IsAllChecked = !IsAllChecked;
        }

        private async Task InstallSongsCommand()
        {
            List<string> songs = new List<string>();
            foreach (FileData file in DownloadWatcher.Data)
            {
                if (file.Checked)
                {
                    songs.Add(file.FilePath);
                    var extension = Path.GetExtension(file.FilePath).ToLower();

                    if (extension == ".zip" || extension == ".rar" || extension == ".7z")
                    {
                        using var archive = Toolbox.OpenArchive(file.FilePath);
                        foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
                        {
                            entry.WriteToDirectory(globalSettings.PhaseshiftMusicDir, new ExtractionOptions
                            {
                                ExtractFullPath = true,
                                Overwrite = true
                            });
                        }
                        File.Delete(file.FilePath);
                    }
                    else
                    {
                        File.Move(file.FilePath, Toolbox.ConstructPath(globalSettings.PhaseshiftDir, file.DisplayName));
                    }
                    // attempt to throttle the system events firing
                    await Task.Delay(500);
                }
            }

            // TODO figure out why the resourcewatchers dont see the change
            var nautilus = new Nautilus(_keystrokeSender, globalSettings.NautilusDirectoryPath);
            await nautilus.RunAsync();
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

        

        public async Task ProcessZipsAsync(CancellationToken cancellationToken)
        {
            try
            {
                var files = Directory.EnumerateFiles(globalSettings.PhaseshiftDir);
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var extension = Path.GetExtension(file).ToLower();

                    if (extension == ".zip" || extension == ".rar" || extension == ".7z")
                    {
                        using var archive = Toolbox.OpenArchive(file);
                        foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
                        {
                            entry.WriteToDirectory(globalSettings.PhaseshiftMusicDir, new ExtractionOptions
                            {
                                ExtractFullPath = true,
                                Overwrite = true
                            });
                        }
                    }
                }
                await Task.Delay(500, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await Shell.Current.DisplayAlert("Info", "Operation was cancelled.", "OK");
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert("Error", $"An error occurred: {ex.Message}", "OK");
            }
        }
    }
}
