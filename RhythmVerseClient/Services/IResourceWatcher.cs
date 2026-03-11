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
        ObservableCollection<FileData> Data { get; set; }
        void LoadItems();
        event EventHandler<string> DirectoryNotFound;
        //event EventHandler<string> ErrorOccurred;
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

        private static async Task<WatcherFileType> GetFileTypeAsync(string filePath)
        {
            byte[] fileSignature = new byte[6];

            try
            {
                using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read);
                _ = await fs.ReadAsync(fileSignature);
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
            else if (fileSignature.Length >= SevenZipSignature.Length && fileSignature.AsSpan()[..SevenZipSignature.Length].SequenceEqual(SevenZipSignature))
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

}
