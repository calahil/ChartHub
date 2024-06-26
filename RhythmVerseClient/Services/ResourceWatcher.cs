using System.Collections.ObjectModel;
using System.Diagnostics;

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
            fileSystemWatcher.Created += OnChanged;
            fileSystemWatcher.Deleted += OnChanged;
            fileSystemWatcher.Renamed += OnChanged;
            fileSystemWatcher.EnableRaisingEvents = true;

            RefreshItems();
        }

        private void OnChanged(object source, FileSystemEventArgs e)
        {
            if (MainThread.IsMainThread)
            {
                RefreshItems();
            }
            else
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    RefreshItems();
                });

            }
        }

        public void RefreshItems()
        {
            if (Directory.Exists(DirectoryPath))
            {
                var existingEntries = new HashSet<string>(Data.Select(d => d.FilePath));

                switch (_watcherType)
                {
                    case WatcherType.Directory:
                        string[] directories = Directory.GetDirectories(DirectoryPath);
                        foreach (string directory in directories)
                        {
                            if (!existingEntries.Contains(directory))
                            {
                                string directoryName = Path.GetFileName(directory);
                                Data.Add(new FileData(directoryName, directory));
                                existingEntries.Add(directory);
                            }
                        }
                        break;
                    case WatcherType.File:
                        string[] files = Directory.GetFiles(DirectoryPath);
                        foreach (string file in files)
                        {
                            if (!existingEntries.Contains(file))
                            {
                                string displayName = Path.GetFileName(file);
                                Data.Add(new FileData(displayName, file));
                                existingEntries.Add(file);
                            }
                        }
                        break;
                }
            }
            else
            {
                DirectoryNotFound?.Invoke(this, DirectoryPath);
            }
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

        public void DeleteItem(int index)
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
                                Directory.Delete(selectedItem.FilePath, true);
                                Data.RemoveAt(index);
                                existingEntries.Remove(selectedItem.FilePath);
                            }
                            break;
                        case WatcherType.File:
                            if (File.Exists(selectedItem.FilePath))
                            {
                                File.Delete(selectedItem.FilePath);
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
        }
    }



    public class FileData(string displayName, string filePath)
    {
        public string DisplayName { get; set; } = displayName;
        public string FilePath { get; set; } = filePath;

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
