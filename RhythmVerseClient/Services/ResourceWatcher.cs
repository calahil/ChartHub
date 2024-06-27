using Microsoft.UI.Xaml.Controls;
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

    public class ResourceWatcher : IResourceWatcher
    {
        public ObservableCollection<FileData> Data { get; set; }
        public string DirectoryPath { get; private set; } = string.Empty;
        private readonly FileSystemWatcher fileSystemWatcher;
        private HashSet<string> existingEntries;
        private WatcherType _watcherType;


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
                UpdateItem(e.Name ?? itemName, e.FullPath, e.OldName ?? oldItemName, e.OldFullPath);
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
            MainThread.BeginInvokeOnMainThread(() =>
            {
                //string itemName = String.Empty;

                if (e.Name == null)
                {
                    //itemName = Path.GetFileName(e.FullPath);
                }
                //UpdateItem(e.Name ?? itemName, e.FullPath, e.OldName ?? oldItemName, e.OldFullPath);
            });
        }

        public void LoadItems()
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
                        var compareData = new FileData(itemName, item);

                        AddItem(itemName, item);
                    }
                }
                //CleanUp();
            }
            else
            {
                DirectoryNotFound?.Invoke(this, DirectoryPath);
            }
        }

        /*private void CleanUp()
        {
            foreach (string item in existingEntries)
            {
                for
                existingEntries.Remove(item);
                string itemName = Path.GetFileName(item);
                var index = Data.IndexOf(new FileData(itemName, item));
                if (index != -1)
                {
                    Data.RemoveAt(index);
                }
            }
        }*/

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

        public void AddItem(string itemName, string itemPath)
        {
            Data.Add(new FileData(itemName, itemPath));
            existingEntries.Add(itemPath);
        }

        public void DeleteItem(string itemName, string itemPath)
        {
            var itemToDelete = new FileData(itemName, itemPath);
            var index = Data.IndexOf(itemToDelete);

            if (index != -1)
            {
                Data.RemoveAt(index);
            }
            if (existingEntries.Contains(itemPath))
            {
                existingEntries.Remove(itemPath);
            }
        }

        public void UpdateItem(string itemName, string itemPath, string oldItemName, string oldItemPath)
        {
            var itemToEdit = Data.FirstOrDefault(item => item.FilePath == oldItemPath);

            if (itemToEdit != null)
            {
                itemToEdit.FilePath = itemPath;
                itemToEdit.DisplayName = itemName;
            }
        }
    }

    public class FileData : INotifyPropertyChanged
    {
        private string _displayName;
        private string _filePath;

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

        public FileData(string displayName, string filePath)
        {
            _displayName = displayName;
            _filePath = filePath;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString()
        {
            return DisplayName;
        }

        public override bool Equals(object obj)
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
