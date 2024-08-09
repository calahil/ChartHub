using RhythmVerseClient.Utilities;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RhythmVerseClient.Services
{
    public enum WatcherType
    {
        Directory,
        File
    }

    public enum WatcherFileType
    {
        Con,
        CloneHero,
        Directory,
        Rar,
        SevenZip,
        Unknown,
        Zip
    }

    public class ResourceWatcher : IResourceWatcher, INotifyPropertyChanged
    {
        private ObservableCollection<FileData> _data;
        public ObservableCollection<FileData> Data
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
            MainThread.BeginInvokeOnMainThread(() =>
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
            MainThread.BeginInvokeOnMainThread(() =>
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
            MainThread.BeginInvokeOnMainThread(async () =>
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

        private static async Task<WatcherFileType> GetFileTypeAsync(string filePath)
        {
            byte[] fileSignature = new byte[6];

            try
            {
                using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read);
                await fs.ReadAsync(fileSignature);
            }
            catch (Exception ex)
            {
                if (Directory.Exists(filePath))
                {
                    return WatcherFileType.CloneHero;
                }
                Console.WriteLine($"Error reading file: {ex.Message}");
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
            else if (File.GetAttributes(filePath).HasFlag(FileAttributes.Directory))
            {
                return WatcherFileType.CloneHero;
            }
            else if (fileSignature.AsSpan()[..SevenZipSignature.Length].SequenceEqual(SevenZipSignature))
            {
                return WatcherFileType.SevenZip;
            }
            else if (Directory.Exists(filePath))
            {
                return WatcherFileType.CloneHero;
            }
            else
            {
                return WatcherFileType.Unknown;
            }
        }

        private static string GetIconForFileType(WatcherFileType fileType)
        {
            return fileType switch
            {
                WatcherFileType.Rar => "rar.png",
                WatcherFileType.Zip => "zip.png",
                WatcherFileType.Con => "rb.png",
                WatcherFileType.SevenZip => "sevenzip.png",
                WatcherFileType.CloneHero => "clonehero.png",
                _ => "blank.png",
            };
        }

        private async Task AddItem(string itemName, string itemPath)
        {
            existingEntries.TryGetValue(itemPath, out var entry);

            if (entry == null)
            {
                var fileType = await GetFileTypeAsync(itemPath);
                var imageFile = GetIconForFileType(fileType);
                long sizeBytes;

                switch (_watcherType)
                {
                    case WatcherType.File:
                        var info = new FileInfo(itemPath);
                        sizeBytes = info.Length;
                        break;
                    case WatcherType.Directory:
                        sizeBytes = Toolbox.GetDirectorySize(itemPath);
                        break;
                    default:
                        sizeBytes = 0;
                        break;
                }
                Data.Add(new FileData(itemName, itemPath, fileType, imageFile, sizeBytes));
                existingEntries.Add(itemPath);
            }
        }

        private void DeleteItem(string itemName, string itemPath)
        {
            var itemToRemove = Data.FirstOrDefault(file => file.DisplayName == itemName);

            if (itemToRemove != null)
            {
                Data.Remove(itemToRemove);
            }
            existingEntries.TryGetValue(itemPath, out var entry);

            if (entry != null)
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
                itemToEdit.FilePath = itemPath;
                itemToEdit.DisplayName = itemName;
            }
            existingEntries.TryGetValue(oldItemPath, out var entry);

            if (entry != null)
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

    public class FileData(string displayName, string filePath, WatcherFileType watcherFileType, string imageFile, long sizeBytes) : INotifyPropertyChanged
    {
        private string _imageFile = imageFile;
        private bool _checked = false;
        private string _displayName = displayName;
        private string _filePath = filePath;
        private WatcherFileType _fileType = watcherFileType;
        private string _fileSize = Toolbox.ConvertFileSize(sizeBytes);
        private long _sizeBytes = sizeBytes;
        private double _downloadProgress = 0;

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked != value)
                {
                    _checked = value;
                    OnPropertyChanged(nameof(Checked));
                }
            }
        }

        public string ImageFile
        {
            get => _imageFile;
            set
            {
                if (_imageFile != value)
                {
                    _imageFile = value;
                    OnPropertyChanged(nameof(ImageFile));
                }
            }
        }

        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (_displayName != value)
                {
                    _displayName = value;
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public WatcherFileType FileType
        {
            get => _fileType;
            set
            {
                if (_fileType != value)
                {
                    _fileType = value;
                    OnPropertyChanged(nameof(FileType));
                }
            }
        }

        public string FileSize
        {
            get => _fileSize;
            set
            {
                if (_fileSize != value)
                {
                    _fileSize = value;
                    OnPropertyChanged(nameof(FileSize));
                }
            }
        }

        public string FilePath
        {
            get => _filePath;
            set
            {
                if (_filePath != value)
                {
                    _filePath = value;
                    OnPropertyChanged(nameof(FilePath));
                }
            }
        }

        public long SizeBytes
        {
            get => _sizeBytes;
            set
            {
                _sizeBytes = value;
                OnPropertyChanged(nameof(SizeBytes));
            }
        }

        public double DownloadProgress
        {
            get => _downloadProgress;
            set
            {
                _downloadProgress = value;
                OnPropertyChanged(nameof(DownloadProgress));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString()
        {
            return DisplayName;
        }

        public override bool Equals(object? obj)
        {
            if (obj is FileData other)
            {
                return this.DisplayName == other.DisplayName && this.FilePath == other.FilePath;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(DisplayName, FilePath);
        }
    }

}
