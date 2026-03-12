using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using RhythmVerseClient.Models;
using RhythmVerseClient.Utilities;

namespace RhythmVerseClient.Services
{
    public interface IResourceWatcher
    {
        string DirectoryPath { get; }
        ObservableCollection<WatcherFile> Data { get; set; }
        void LoadItems();
        event EventHandler<string> DirectoryNotFound;
        //event EventHandler<string> ErrorOccurred;
    }

    public class ResourceWatcher : IResourceWatcher, INotifyPropertyChanged
    {
        private ObservableCollection<WatcherFile> _data;
        public ObservableCollection<WatcherFile> Data
        {
            get => _data;
            set
            {
                _data = value;
                OnPropertyChanged();
            }
        }

        private string _directoryPath;
        public string DirectoryPath
        {
            get => _directoryPath;
            set
            {
                _directoryPath = value;
                OnPropertyChanged();
            }
        }

        private readonly FileSystemWatcher fileSystemWatcher;
        private HashSet<string> existingEntries;
        private readonly WatcherType _watcherType;


        private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];
        private static readonly byte[] RarSignature = "Rar!"u8.ToArray();
        private static readonly byte[] Rb3ConSignature = "CON"u8.ToArray();
        private static readonly byte[] SevenZipSignature = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];

        public event EventHandler<string>? DirectoryNotFound;
        //public event EventHandler<string>? ErrorOccurred;

        public ResourceWatcher(string path, WatcherType watcherType)
        {
            _directoryPath = path;
            _watcherType = watcherType;
            _data = [];
            fileSystemWatcher = new FileSystemWatcher();
            existingEntries = [];
            fileSystemWatcher.Path = DirectoryPath;
            fileSystemWatcher.NotifyFilter = NotifyFilters.LastAccess | NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName;
            fileSystemWatcher.Filter = "*.*";
            fileSystemWatcher.Changed += OnChanged;
            fileSystemWatcher.Created += OnCreated;
            fileSystemWatcher.Deleted += OnDeleted;
            fileSystemWatcher.Renamed += OnRenamed;
            fileSystemWatcher.EnableRaisingEvents = true;
        }

        public async void LoadItems()
        {
            if (Directory.Exists(DirectoryPath))
            {
                string[] items = [];

                switch (_watcherType)
                {
                    case WatcherType.Directory:
                        items = Directory.GetDirectories(DirectoryPath);

                        break;
                    case WatcherType.File:
                        items = Directory.GetFiles(DirectoryPath);

                        break;
                }
                foreach (string item in items)
                {
                    // TODO: needs error handling or does CleanUp do a good enough job
                    if (!existingEntries.Contains(item))
                    {
                        var itemName = Path.GetFileName(item);
                        //var fileType = await

                        await AddItem(itemName, item);
                    }
                }
                //CleanUp();
            }
            else
            {
                DirectoryNotFound?.Invoke(this, DirectoryPath);
            }
        }

        private void OnRenamed(object sender, RenamedEventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                string itemName = String.Empty;
                string oldItemName = String.Empty;

                if (e.Name == null || e.OldName == null)
                {
                    itemName = Path.GetFileName(e.FullPath);
                    oldItemName = Path.GetFileName(e.OldFullPath);
                }
                UpdateItem(e.Name ?? itemName, e.FullPath, e.OldFullPath).RunSynchronously();
            });
        }

        private void OnDeleted(object sender, FileSystemEventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    string itemName = String.Empty;
                    if (e.Name == null)
                    {
                        itemName = Path.GetFileName(e.FullPath);
                    }
                    DeleteItem(e.Name ?? itemName, e.FullPath);
                }
                catch (Exception ex)
                {
                    Logger.LogMessage($"An error occurred: {ex.Message}");
                }
            });
        }

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                string itemName = String.Empty;
                if (e.Name == null)
                {
                    itemName = Path.GetFileName(e.FullPath);
                }

                await AddItem(e.Name ?? itemName, e.FullPath);
            });

        }

        private void OnChanged(object source, FileSystemEventArgs e)
        {
            /*MainThread.BeginInvokeOnMainThread(() =>
            {
                //string itemName = String.Empty;

                if (e.Name == null)
                {
                    //itemName = Path.GetFileName(e.FullPath);
                }
                //UpdateItem(e.Name ?? itemName, e.FullPath, e.OldName ?? oldItemName, e.OldFullPath);
            });*/
        }

        internal static async Task<WatcherFileType> GetFileTypeForSnapshotAsync(string filePath)
        {
            if (Directory.Exists(filePath))
            {
                return WatcherFileType.CloneHero;
            }

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (extension == ".zip")
            {
                return WatcherFileType.Zip;
            }
            if (extension == ".rar")
            {
                return WatcherFileType.Rar;
            }
            if (extension == ".7z")
            {
                return WatcherFileType.SevenZip;
            }

            byte[] fileSignature = new byte[6];

            try
            {
                using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                _ = await fs.ReadAsync(fileSignature);
            }
            catch (UnauthorizedAccessException)
            {
                return WatcherFileType.Unknown;
            }
            catch (Exception ex)
            {
                if (Directory.Exists(filePath))
                {
                    return WatcherFileType.CloneHero;
                }
                Logger.LogMessage($"Error reading file metadata: {ex.Message}");
                return WatcherFileType.Unknown;
            }

            if (fileSignature.Length >= ZipSignature.Length && fileSignature.AsSpan()[..ZipSignature.Length].SequenceEqual(ZipSignature))
            {
                return WatcherFileType.Zip;
            }
            else if (fileSignature.Length >= RarSignature.Length && fileSignature.AsSpan()[..RarSignature.Length].SequenceEqual(RarSignature))
            {
                return WatcherFileType.Rar;
            }
            else if (fileSignature.Length >= Rb3ConSignature.Length && fileSignature.AsSpan()[..Rb3ConSignature.Length].SequenceEqual(Rb3ConSignature))
            {
                return WatcherFileType.Con;
            }
            else if (fileSignature.Length >= SevenZipSignature.Length && fileSignature.AsSpan()[..SevenZipSignature.Length].SequenceEqual(SevenZipSignature))
            {
                return WatcherFileType.SevenZip;
            }
            else
            {
                return WatcherFileType.Unknown;
            }
        }

        private static async Task<WatcherFileType> GetFileTypeAsync(string filePath)
        {
            return await GetFileTypeForSnapshotAsync(filePath);
        }

        internal static string GetIconForSnapshot(WatcherFileType fileType)
        {
            var iconFileName = fileType switch
            {
                WatcherFileType.Rar => "rar.png",
                WatcherFileType.Zip => "zip.png",
                WatcherFileType.Con => "rb.png",
                WatcherFileType.SevenZip => "sevenzip.png",
                WatcherFileType.CloneHero => "clonehero.png",
                _ => "blank.png",
            };

            return $"avares://RhythmVerseClient/Resources/Images/{iconFileName}";
        }

        private static string GetIconForFileType(WatcherFileType fileType)
        {
            return GetIconForSnapshot(fileType);
        }

        private async Task AddItem(string itemName, string itemPath)
        {
            if (!existingEntries.Contains(itemPath))
            {
                var fileType = await GetFileTypeAsync(itemPath);
                var imageFile = GetIconForFileType(fileType);
                long sizeBytes;

                switch (_watcherType)
                {
                    case WatcherType.File:
                        try
                        {
                            var info = new FileInfo(itemPath);
                            sizeBytes = info.Length;
                        }
                        catch (UnauthorizedAccessException)
                        {
                            sizeBytes = 0;
                        }
                        break;
                    case WatcherType.Directory:
                        sizeBytes = FileTools.GetDirectorySize(itemPath);
                        break;
                    default:
                        sizeBytes = 0;
                        break;
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!existingEntries.Contains(itemPath))
                    {
                        Data.Add(new WatcherFile(itemName, itemPath, fileType, imageFile, sizeBytes));
                        existingEntries.Add(itemPath);
                    }
                });
            }
        }

        private void DeleteItem(string itemName, string itemPath)
        {
            var itemToRemove = Data.FirstOrDefault(file => file.DisplayName == itemName);

            if (itemToRemove != null)
            {
                Data.Remove(itemToRemove);
            }

            if (existingEntries.Contains(itemPath))
            {
                existingEntries.Remove(itemPath);
            }
        }

        private async Task UpdateItem(string itemName, string itemPath, string oldItemPath)
        {
            var itemToEdit = Data.FirstOrDefault(item => item.FilePath == oldItemPath);

            if (itemToEdit != null)
            {
                itemToEdit.FileType = await GetFileTypeAsync(itemPath);
                var imageFile = GetIconForFileType(itemToEdit.FileType);
                itemToEdit.ImageFile = imageFile;
                itemToEdit.FilePath = itemPath;
                itemToEdit.DisplayName = itemName;
            }

            if (existingEntries.Contains(oldItemPath))
            {
                existingEntries.Remove(oldItemPath);
            }
            existingEntries.Add(itemPath);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class SnapshotResourceWatcher : IResourceWatcher, INotifyPropertyChanged
    {
        private readonly WatcherType _watcherType;
        private ObservableCollection<WatcherFile> _data = [];

        public string DirectoryPath { get; }

        public ObservableCollection<WatcherFile> Data
        {
            get => _data;
            set
            {
                _data = value;
                OnPropertyChanged();
            }
        }

        public event EventHandler<string>? DirectoryNotFound;
        public event PropertyChangedEventHandler? PropertyChanged;

        public SnapshotResourceWatcher(string path, WatcherType watcherType)
        {
            DirectoryPath = path;
            _watcherType = watcherType;
        }

        public async void LoadItems()
        {
            if (!Directory.Exists(DirectoryPath))
            {
                DirectoryNotFound?.Invoke(this, DirectoryPath);
                return;
            }

            string[] items = _watcherType switch
            {
                WatcherType.Directory => Directory.GetDirectories(DirectoryPath),
                WatcherType.File => Directory.GetFiles(DirectoryPath),
                _ => []
            };

            var files = new List<WatcherFile>();
            foreach (var item in items)
            {
                try
                {
                    var itemName = Path.GetFileName(item);
                    var itemType = await ResourceWatcher.GetFileTypeForSnapshotAsync(item);
                    var imageFile = ResourceWatcher.GetIconForSnapshot(itemType);
                    long sizeBytes = _watcherType == WatcherType.Directory
                        ? FileTools.GetDirectorySize(item)
                        : new FileInfo(item).Length;

                    files.Add(new WatcherFile(itemName, item, itemType, imageFile, sizeBytes));
                }
                catch (Exception ex)
                {
                    Logger.LogMessage($"SnapshotResourceWatcher skipped '{item}': {ex.Message}");
                }
            }

            Dispatcher.UIThread.Post(() => Data = new ObservableCollection<WatcherFile>(files));
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// An <see cref="IResourceWatcher"/> backed by a Google Drive folder.
    /// Loads files on construction and then polls at <see cref="PollingInterval"/>,
    /// adding/removing entries in <see cref="Data"/> to match the remote folder.
    /// </summary>
    public class GoogleDriveWatcher : IResourceWatcher, INotifyPropertyChanged, IAsyncDisposable
    {
        private readonly IGoogleDriveClient _driveClient;
        private readonly CancellationTokenSource _cts = new();
        private Task? _pollTask;

        public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(30);

        // IResourceWatcher.DirectoryPath surfaces the Drive folder ID.
        public string DirectoryPath => _driveClient.RhythmVerseFolderId;

        private ObservableCollection<WatcherFile> _data = [];
        public ObservableCollection<WatcherFile> Data
        {
            get => _data;
            set { _data = value; OnPropertyChanged(); }
        }

        public event EventHandler<string>? DirectoryNotFound;

        private static readonly string[] KnownExtensions = [".zip", ".rar", ".7z"];

        public GoogleDriveWatcher(IGoogleDriveClient driveClient)
        {
            _driveClient = driveClient;
        }

        /// <summary>
        /// Performs an initial load then starts background polling.
        /// Call this once after construction (e.g. from InitializeAsync in the ViewModel).
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_driveClient.RhythmVerseFolderId))
            {
                DirectoryNotFound?.Invoke(this, "RhythmVerse folder not initialised on Google Drive.");
                return;
            }

            _ = LoadItemsAsync(cancellationToken);
            _pollTask = PollAsync(_cts.Token);
        }

        // Synchronous IResourceWatcher.LoadItems() — kicks off an async load and returns immediately.
        public void LoadItems()
        {
            if (string.IsNullOrWhiteSpace(_driveClient.RhythmVerseFolderId))
            {
                DirectoryNotFound?.Invoke(this, "RhythmVerse folder not initialised on Google Drive.");
                return;
            }

            _ = LoadItemsAsync(CancellationToken.None);
        }

        private async Task LoadItemsAsync(CancellationToken cancellationToken)
        {
            IList<Google.Apis.Drive.v3.Data.File> files;
            try
            {
                files = await _driveClient.ListFilesAsync(_driveClient.RhythmVerseFolderId);
            }
            catch (Exception ex)
            {
                Logger.LogMessage($"GoogleDriveWatcher: failed to list files – {ex.Message}");
                return;
            }

            var seenIds = new HashSet<string>(files.Select(f => f.Id));

            // Remove entries that are no longer on Drive
            var toRemove = Data.Where(w => !seenIds.Contains(w.FilePath)).ToList();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var item in toRemove)
                    Data.Remove(item);
            });

            // Add new entries
            var existingIds = new HashSet<string>(Data.Select(w => w.FilePath));
            foreach (var file in files)
            {
                if (existingIds.Contains(file.Id))
                    continue;

                var watcherFile = await BuildWatcherFileAsync(file);
                await Dispatcher.UIThread.InvokeAsync(() => Data.Add(watcherFile));
            }
        }

        private async Task PollAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(PollingInterval, cancellationToken);
                    await LoadItemsAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.LogMessage($"GoogleDriveWatcher poll error: {ex.Message}");
                }
            }
        }

        private static async Task<WatcherFile> BuildWatcherFileAsync(Google.Apis.Drive.v3.Data.File file)
        {
            var fileType = DetermineFileType(file.Name);
            var imageFile = GetIconForFileType(fileType);

            // Size is included via ListFilesAsync which requests fields(id, name, size, mimeType).
            long sizeBytes = file.Size ?? 0;

            return await Task.FromResult(new WatcherFile(
                displayName: file.Name,
                filePath: file.Id,
                watcherFileType: fileType,
                imageFile: imageFile,
                sizeBytes: sizeBytes));
        }

        private static WatcherFileType DetermineFileType(string fileName)
        {
            return Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".zip" => WatcherFileType.Zip,
                ".rar" => WatcherFileType.Rar,
                ".7z" => WatcherFileType.SevenZip,
                "" => WatcherFileType.Con,
                _ => WatcherFileType.Unknown,
            };
        }

        private static string GetIconForFileType(WatcherFileType fileType)
        {
            var iconFileName = fileType switch
            {
                WatcherFileType.Rar => "rar.png",
                WatcherFileType.Zip => "zip.png",
                WatcherFileType.Con => "rb.png",
                WatcherFileType.SevenZip => "sevenzip.png",
                WatcherFileType.CloneHero => "clonehero.png",
                _ => "blank.png",
            };
            return $"avares://RhythmVerseClient/Resources/Images/{iconFileName}";
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            if (_pollTask is not null)
            {
                try { await _pollTask; } catch (OperationCanceledException) { }
            }
            _cts.Dispose();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

}
