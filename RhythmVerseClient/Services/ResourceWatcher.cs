using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.CustomAttributes;
using RhythmVerseClient.Utilities;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace RhythmVerseClient.Services
{
    public enum WatcherType
    {
        Directory,
        File
    }

    public enum WatcherFileType
    {
        Rar,
        Zip,
        Con,
        CloneHero,
        SevenZip,
        Unknown
    }

    public class ResourceWatcher : IResourceWatcher
    {
        public ObservableCollection<FileData> Data { get; set; }
        public string DirectoryPath { get; private set; } = string.Empty;
        private readonly FileSystemWatcher fileSystemWatcher;
        private HashSet<string> existingEntries;
        private WatcherType _watcherType;


        private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];
        private static readonly byte[] RarSignature = "Rar!"u8.ToArray();
        private static readonly byte[] Rb3ConSignature = "CON"u8.ToArray();
        private static readonly byte[] SevenZipSignature = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];

        public event EventHandler<string>? DirectoryNotFound;
        public event EventHandler<string>? ErrorOccurred;

        public ResourceWatcher()
        {
            Data = [];
            fileSystemWatcher = new FileSystemWatcher();
            existingEntries = [];
        }

        public void Initialize(string path, WatcherType watcherType)
        {
            DirectoryPath = path;
            _watcherType = watcherType;

            fileSystemWatcher.Path = DirectoryPath;
            fileSystemWatcher.NotifyFilter = NotifyFilters.LastAccess | NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName;
            fileSystemWatcher.Filter = "*.*";
            fileSystemWatcher.Changed += OnChanged;
            fileSystemWatcher.Created += OnCreated;
            fileSystemWatcher.Deleted += OnDeleted;
            fileSystemWatcher.Renamed += OnRenamed;
            fileSystemWatcher.EnableRaisingEvents = true;

            //RefreshItems();
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
                string itemName = String.Empty;
                if (e.Name == null)
                {
                    itemName = Path.GetFileName(e.FullPath);
                }
                DeleteItem(e.Name ?? itemName, e.FullPath);
            });
        }

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                string itemName = String.Empty;
                if (e.Name == null)
                {
                    itemName = Path.GetFileName(e.FullPath);
                }

                AddItem(e.Name ?? itemName, e.FullPath);
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

        public static async Task<WatcherFileType> GetFileTypeAsync(string filePath)
        {
            byte[] fileSignature = new byte[6];

            try
            {
                using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read);
                await fs.ReadAsync(fileSignature, 0, fileSignature.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading file: {ex.Message}");
                return WatcherFileType.Unknown;
            }

            if (fileSignature.Length >= ZipSignature.Length && fileSignature.AsSpan().Slice(0, ZipSignature.Length).SequenceEqual(ZipSignature))
            {
                return WatcherFileType.Zip;
            }
            else if (fileSignature.Length >= RarSignature.Length && fileSignature.AsSpan().Slice(0, RarSignature.Length).SequenceEqual(RarSignature))
            {
                return WatcherFileType.Rar;
            }
            else if (fileSignature.Length >= Rb3ConSignature.Length && fileSignature.AsSpan().Slice(0, Rb3ConSignature.Length).SequenceEqual(Rb3ConSignature))
            {
                return WatcherFileType.Con;
            }
            else if (File.GetAttributes(filePath).HasFlag(FileAttributes.Directory))
            {
                return WatcherFileType.CloneHero;
            }
            else if (fileSignature.AsSpan().Slice(0, SevenZipSignature.Length).SequenceEqual(SevenZipSignature))
            {
                return WatcherFileType.SevenZip;
            }
            else
            {
                return WatcherFileType.Unknown;
            }
        }

        public static string GetIconForFileType(WatcherFileType fileType)
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

        public int GetItemCount()
        {
            if (Directory.Exists(DirectoryPath))
            {
                return _watcherType switch
                {
                    WatcherType.Directory => Directory.GetDirectories(DirectoryPath).Length,
                    WatcherType.File => Directory.GetFiles(DirectoryPath).Length,
                    _ => 0,
                };
            }
            else
            {
                DirectoryNotFound?.Invoke(this, DirectoryPath);
                return 0;
            }
        }

        public void OpenLocation(int index)
        {
            var selectedItem = Data[index];
            string? directoryPath = _watcherType == WatcherType.Directory ? selectedItem.FilePath : Path.GetDirectoryName(selectedItem.FilePath);

            try
            {
                if (Directory.Exists(directoryPath))
                {
                    Process.Start("explorer.exe", directoryPath);
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Error opening file location: {ex.Message}");
            }
        }

        public void DeleteFile(int index)
        {
            var selectedItem = Data[index];

            if (Directory.Exists(DirectoryPath))
            {
                try
                {
                    switch (_watcherType)
                    {
                        case WatcherType.Directory:
                            if (Directory.Exists(selectedItem.FilePath))
                            {
                                //Directory.Delete(selectedItem.FilePath, true);
                                Data.RemoveAt(index);
                                existingEntries.Remove(selectedItem.FilePath);
                            }
                            break;
                        case WatcherType.File:
                            if (File.Exists(selectedItem.FilePath))
                            {
                                //File.Delete(selectedItem.FilePath);
                                Data.RemoveAt(index);
                                existingEntries.Remove(selectedItem.FilePath);
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, $"Failed to delete the item: {ex.Message}");
                }
            }
            else
            {
                DirectoryNotFound?.Invoke(this, DirectoryPath);
            }
        }

        public async Task AddItem(string itemName, string itemPath)
        {
            var fileType = await GetFileTypeAsync(itemPath);
            var imageFile = GetIconForFileType(fileType);
            string fileSize;

            switch (_watcherType)
                {
                case WatcherType.File:
                    var info = new FileInfo(itemPath);
                    fileSize = Toolbox.ConvertFileSize(info.Length);
                    break;
                case WatcherType.Directory:
                    fileSize = Toolbox.ConvertFileSize(Toolbox.GetDirectorySize(itemPath));
                    break;
                default:
                    fileSize = "0B";
                    break;
            }            
            Data.Add(new FileData(itemName, itemPath, fileType, imageFile, fileSize));
            existingEntries.Add(itemPath);
        }

        public async Task DeleteItem(string itemName, string itemPath)
        {
            var fileType = await GetFileTypeAsync(itemPath);
            var imageFile = GetIconForFileType(fileType);
            var info = new FileInfo(itemPath);
            var fileSize = Toolbox.ConvertFileSize(info.Length);
            var itemToDelete = new FileData(itemName, itemPath, fileType, imageFile, fileSize);
            var index = Data.IndexOf(itemToDelete);

            if (index != -1)
            {
                Data.RemoveAt(index);
            }
            existingEntries.Remove(itemPath);
        }

        public async Task UpdateItem(string itemName, string itemPath, string oldItemPath)
        {
            var itemToEdit = Data.FirstOrDefault(item => item.FilePath == oldItemPath);

            if (itemToEdit != null)
            {
                itemToEdit.FileType = await GetFileTypeAsync(itemPath);
                var imageFile = GetIconForFileType(itemToEdit.FileType);
                itemToEdit.FilePath = itemPath;
                itemToEdit.DisplayName = itemName;
            }
        }
    }

    public class FileData(string displayName, string filePath, WatcherFileType watcherFileType, string imageFile, string fileSize) : INotifyPropertyChanged
    {
        private string _displayName = displayName;
        private string _filePath = filePath;
        private string _imageFile = imageFile;
        private WatcherFileType _fileType = watcherFileType;
        private string _fileSize = fileSize;

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
