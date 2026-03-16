using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using ChartHub.Models;
using ChartHub.Utilities;

namespace ChartHub.Services
{
    public class ResourceWatcher : IResourceWatcher, INotifyPropertyChanged, IDisposable
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

        public void LoadItems()
        {
            _ = LoadItemsAsync();
        }

        private async Task LoadItemsAsync()
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

                foreach (var item in items)
                {
                    if (!existingEntries.Contains(item))
                    {
                        var itemName = Path.GetFileName(item);
                        await AddItem(itemName, item);
                    }
                }
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
                var itemName = e.Name ?? Path.GetFileName(e.FullPath);
                _ = UpdateItem(itemName, e.FullPath, e.OldFullPath);
            });
        }

        private void OnDeleted(object sender, FileSystemEventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var itemName = e.Name ?? Path.GetFileName(e.FullPath);
                    DeleteItem(itemName, e.FullPath);
                }
                catch (Exception ex)
                {
                    Logger.LogError("Watcher", "Resource watcher delete event failed", ex, new Dictionary<string, object?>
                    {
                        ["directoryPath"] = DirectoryPath,
                        ["itemPath"] = e.FullPath,
                    });
                }
            });
        }

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                var itemName = e.Name ?? Path.GetFileName(e.FullPath);
                await AddItem(itemName, e.FullPath);
            });
        }

        private void OnChanged(object source, FileSystemEventArgs e)
        {
            // Intentionally unused at the moment.
        }

        internal static async Task<WatcherFileType> GetFileTypeForSnapshotAsync(string filePath)
        {
            if (Directory.Exists(filePath))
                return WatcherFileType.CloneHero;

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (extension == ".zip")
                return WatcherFileType.Zip;
            if (extension == ".rar")
                return WatcherFileType.Rar;
            if (extension == ".7z")
                return WatcherFileType.SevenZip;

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
                    return WatcherFileType.CloneHero;

                Logger.LogError("Watcher", "Resource watcher failed to read file metadata", ex, new Dictionary<string, object?>
                {
                    ["filePath"] = filePath,
                });
                return WatcherFileType.Unknown;
            }

            if (fileSignature.Length >= ZipSignature.Length && fileSignature.AsSpan()[..ZipSignature.Length].SequenceEqual(ZipSignature))
                return WatcherFileType.Zip;
            if (fileSignature.Length >= RarSignature.Length && fileSignature.AsSpan()[..RarSignature.Length].SequenceEqual(RarSignature))
                return WatcherFileType.Rar;
            if (fileSignature.Length >= Rb3ConSignature.Length && fileSignature.AsSpan()[..Rb3ConSignature.Length].SequenceEqual(Rb3ConSignature))
                return WatcherFileType.Con;
            if (fileSignature.Length >= SevenZipSignature.Length && fileSignature.AsSpan()[..SevenZipSignature.Length].SequenceEqual(SevenZipSignature))
                return WatcherFileType.SevenZip;

            return WatcherFileType.Unknown;
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

            return $"avares://ChartHub/Resources/Images/{iconFileName}";
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
                Data.Remove(itemToRemove);

            if (existingEntries.Contains(itemPath))
                existingEntries.Remove(itemPath);
        }

        private async Task UpdateItem(string itemName, string itemPath, string oldItemPath)
        {
            try
            {
                var fileType = await GetFileTypeAsync(itemPath).ConfigureAwait(false);
                var imageFile = GetIconForFileType(fileType);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var itemToEdit = Data.FirstOrDefault(item => item.FilePath == oldItemPath);

                    if (itemToEdit != null)
                    {
                        itemToEdit.FileType = fileType;
                        itemToEdit.ImageFile = imageFile;
                        itemToEdit.FilePath = itemPath;
                        itemToEdit.DisplayName = itemName;
                    }

                    if (existingEntries.Contains(oldItemPath))
                        existingEntries.Remove(oldItemPath);

                    existingEntries.Add(itemPath);
                });
            }
            catch (Exception ex)
            {
                Logger.LogError("Watcher", "Resource watcher rename update failed", ex, new Dictionary<string, object?>
                {
                    ["directoryPath"] = DirectoryPath,
                    ["itemPath"] = itemPath,
                    ["oldItemPath"] = oldItemPath,
                });
            }
        }

        public void Dispose()
        {
            fileSystemWatcher.EnableRaisingEvents = false;
            fileSystemWatcher.Changed -= OnChanged;
            fileSystemWatcher.Created -= OnCreated;
            fileSystemWatcher.Deleted -= OnDeleted;
            fileSystemWatcher.Renamed -= OnRenamed;
            fileSystemWatcher.Dispose();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
