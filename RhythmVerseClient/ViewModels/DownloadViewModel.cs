using CommunityToolkit.Mvvm.Input;
using RhythmVerseClient.Models;
using RhythmVerseClient.Services;
using RhythmVerseClient.Strings;
using RhythmVerseClient.Utilities;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace RhythmVerseClient.ViewModels
{
    public class DownloadViewModel : INotifyPropertyChanged
    {
        private readonly AppGlobalSettings globalSettings;
        public bool IsCompanionMode => OperatingSystem.IsAndroid();
        public bool IsDesktopMode => !OperatingSystem.IsAndroid();

        public IResourceWatcher DownloadWatcher { get; set; }
        public GoogleDriveWatcher GoogleWatcher { get; set; }

        public ICommand CheckAllCommand { get; }
        public IAsyncRelayCommand InstallSongs { get; }
        public IAsyncRelayCommand UploadCloud { get; }

        private bool _isAnyChecked;
        public bool IsAnyChecked
        {
            get => _isAnyChecked;
            set
            {
                if (_isAnyChecked != value)
                {
                    _isAnyChecked = value;
                    OnPropertyChanged(nameof(IsAnyChecked));
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
                    OnPropertyChanged(nameof(IsAllChecked));
                    CheckAllItems(value);
                }
            }
        }

        private WatcherFile? _selectedFile;
        public WatcherFile? SelectedFile
        {
            get => _selectedFile;
            set
            {
                _selectedFile = value;
                OnPropertyChanged(nameof(SelectedFile));
            }
        }

        public ObservableCollection<WatcherFile> DownloadFiles { get; set; }
        public ObservableCollection<WatcherFile> GoogleFiles { get; set; }

        private DownloadPageStrings _pageStrings;
        public DownloadPageStrings PageStrings
        {
            get { return _pageStrings; }
            set
            {
                if (_pageStrings != value)
                {
                    _pageStrings = value;
                    OnPropertyChanged(nameof(PageStrings));
                }
            }
        }

        private IGoogleDriveClient _googleDrive;

        public DownloadViewModel(AppGlobalSettings settings, IGoogleDriveClient googleDrive)
        {
            globalSettings = settings;
            _googleDrive = googleDrive;
            DownloadWatcher = OperatingSystem.IsAndroid()
                ? new SnapshotResourceWatcher(globalSettings.DownloadDir, WatcherType.File)
                : new ResourceWatcher(globalSettings.DownloadDir, WatcherType.File);
            GoogleWatcher = new GoogleDriveWatcher(_googleDrive);
            CheckAllCommand = new RelayCommand(CheckAllItemsCommand);
            InstallSongs = new AsyncRelayCommand(InstallSongsCommand);
            UploadCloud = new AsyncRelayCommand(UploadCloudCommand);
            DownloadFiles = DownloadWatcher.Data;
            GoogleFiles = GoogleWatcher.Data;
            _pageStrings = new DownloadPageStrings();

            DownloadFiles.CollectionChanged += DownloadFiles_CollectionChanged;
        }

        private void DownloadFiles_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            foreach (WatcherFile file in DownloadFiles)
            {
                file.PropertyChanged += ItemPropertyChanged;
            }
        }

        private void ItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(WatcherFile.Checked))
            {
                IsAnyChecked = AnyItemChecked();
            }
        }

        private void CheckAllItemsCommand()
        {
            IsAllChecked = !IsAllChecked;
        }

        public async Task UploadCloudCommand()
        {
            List<string> items = [];
            if (string.IsNullOrWhiteSpace(_googleDrive.RhythmVerseFolderId))
            {
                throw new InvalidOperationException("Google Drive is not initialized.");
            }
            foreach (WatcherFile file in DownloadFiles)
            {
                if (file.Checked)
                {
                    try
                    {
                        if (!File.Exists(file.FilePath))
                        {
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"File {file.FilePath} does not exist: {ex.Message}");
                        continue;
                    }
                    try
                    {
                        await _googleDrive.UploadFileAsync(_googleDrive.RhythmVerseFolderId, file.FilePath);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to upload {file.FilePath}: {ex.Message}");
                    }
                }

            }
        }

        public async Task InstallSongsCommand()
        {
            List<string> items = [];

            foreach (WatcherFile file in DownloadFiles)
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
                var newFilePath = Path.Combine(globalSettings.StagingDir, displayName);

                File.Move(file, newFilePath);

                await Task.Delay(100);
            }

            // TODO: Convert UI tab navigation to Avalonia
            // var mainPage = Application.Current?.MainPage as MainPage;
            // mainPage?.FocusOnTab(3);

        }

        public void CheckAllItems(bool isChecked)
        {
            foreach (var item in DownloadFiles)
            {
                item.Checked = isChecked;
            }
            OnPropertyChanged(nameof(DownloadFiles)); // Notify the UI to update
        }

        public bool AnyItemChecked()
        {
            return DownloadFiles.Any(item => item.Checked);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

    }
}
