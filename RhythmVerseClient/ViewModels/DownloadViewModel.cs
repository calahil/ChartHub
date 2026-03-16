using CommunityToolkit.Mvvm.Input;
using RhythmVerseClient.Models;
using RhythmVerseClient.Services;
using RhythmVerseClient.Services.Transfers;
using RhythmVerseClient.Strings;
using RhythmVerseClient.Utilities;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace RhythmVerseClient.ViewModels
{
    public class DownloadViewModel : INotifyPropertyChanged, IAsyncDisposable
    {
        private readonly AppGlobalSettings globalSettings;
        public bool IsCompanionMode => OperatingSystem.IsAndroid();
        public bool IsDesktopMode => !OperatingSystem.IsAndroid();

        public IResourceWatcher DownloadWatcher { get; set; }
        public GoogleDriveWatcher GoogleWatcher { get; set; }

        public ICommand CheckAllCommand { get; }
        public IAsyncRelayCommand InstallSongs { get; }
        public IAsyncRelayCommand UploadCloud { get; }
        public IAsyncRelayCommand DownloadCloudToLocal { get; }
        public IAsyncRelayCommand SyncCloudToLocal { get; }

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

        private bool _isAnyCloudChecked;
        public bool IsAnyCloudChecked
        {
            get => _isAnyCloudChecked;
            set
            {
                if (_isAnyCloudChecked != value)
                {
                    _isAnyCloudChecked = value;
                    OnPropertyChanged(nameof(IsAnyCloudChecked));
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
        private readonly ITransferOrchestrator _transferOrchestrator;

        public DownloadViewModel(AppGlobalSettings settings, IGoogleDriveClient googleDrive, ITransferOrchestrator transferOrchestrator)
        {
            globalSettings = settings;
            _googleDrive = googleDrive;
            _transferOrchestrator = transferOrchestrator;
            DownloadWatcher = OperatingSystem.IsAndroid()
                ? new SnapshotResourceWatcher(globalSettings.DownloadDir, WatcherType.File)
                : new ResourceWatcher(globalSettings.DownloadDir, WatcherType.File);
            GoogleWatcher = new GoogleDriveWatcher(_googleDrive);
            CheckAllCommand = new RelayCommand(CheckAllItemsCommand);
            InstallSongs = new AsyncRelayCommand(InstallSongsCommand);
            UploadCloud = new AsyncRelayCommand(UploadCloudCommand);
            DownloadCloudToLocal = new AsyncRelayCommand(DownloadCloudToLocalCommand);
            SyncCloudToLocal = new AsyncRelayCommand(SyncCloudToLocalCommand);
            DownloadFiles = DownloadWatcher.Data;
            GoogleFiles = GoogleWatcher.Data;
            _pageStrings = new DownloadPageStrings();

            DownloadFiles.CollectionChanged += DownloadFiles_CollectionChanged;
            GoogleFiles.CollectionChanged += GoogleFiles_CollectionChanged;
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
                IsAnyCloudChecked = AnyCloudItemChecked();
            }
        }

        private void GoogleFiles_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            foreach (WatcherFile file in GoogleFiles)
            {
                file.PropertyChanged += ItemPropertyChanged;
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
                        Logger.LogWarning("Transfer", "Local file could not be inspected before upload", new Dictionary<string, object?>
                        {
                            ["filePath"] = file.FilePath,
                            ["reason"] = ex.GetType().Name,
                        });
                        Logger.LogError("Transfer", "Local file inspection failed before upload", ex, new Dictionary<string, object?>
                        {
                            ["filePath"] = file.FilePath,
                        });
                        continue;
                    }
                    try
                    {
                        await _googleDrive.UploadFileAsync(_googleDrive.RhythmVerseFolderId, file.FilePath);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("Transfer", "Failed to upload local file to Google Drive", ex, new Dictionary<string, object?>
                        {
                            ["filePath"] = file.FilePath,
                            ["folderId"] = _googleDrive.RhythmVerseFolderId,
                        });
                    }
                }

            }
        }

        public async Task DownloadCloudToLocalCommand()
        {
            var selected = GoogleFiles.Where(file => file.Checked).ToList();
            if (selected.Count == 0)
                return;

            await _transferOrchestrator.DownloadSelectedCloudFilesToLocalAsync(selected);
            DownloadWatcher.LoadItems();
        }

        public async Task SyncCloudToLocalCommand()
        {
            await _transferOrchestrator.SyncCloudToLocalAdditiveAsync(GoogleFiles);
            DownloadWatcher.LoadItems();
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

        public bool AnyCloudItemChecked()
        {
            return GoogleFiles.Any(item => item.Checked);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public async ValueTask DisposeAsync()
        {
            DownloadFiles.CollectionChanged -= DownloadFiles_CollectionChanged;
            GoogleFiles.CollectionChanged -= GoogleFiles_CollectionChanged;

            foreach (var file in DownloadFiles)
                file.PropertyChanged -= ItemPropertyChanged;

            foreach (var file in GoogleFiles)
                file.PropertyChanged -= ItemPropertyChanged;

            if (DownloadWatcher is IDisposable disposableWatcher)
                disposableWatcher.Dispose();

            await GoogleWatcher.DisposeAsync();
        }

    }
}
