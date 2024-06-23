using System.Collections.ObjectModel;
using System.Diagnostics;

namespace RhythmVerseClient.Services
{
    public enum WatcherType
    {
        Directory,
        File
    }

    public class ResourceWatcher
    {
        public ObservableCollection<FileData> Data { get; set; }
        public string DirectoryPath { get; set; }
        private readonly FileSystemWatcher fileSystemWatcher;
        // Hash check for our filenames to stop duplicates
        private HashSet<string> existingEntries;
        private WatcherType _watcherType;
        private MainPage _mainPage;

        public ResourceWatcher(string path, WatcherType watcherType, MainPage mainPage)
        {
            DirectoryPath = path;
            _mainPage = mainPage;
            _watcherType = watcherType;
            Data = [];
            fileSystemWatcher = new FileSystemWatcher
            {
                Path = DirectoryPath,
                NotifyFilter = NotifyFilters.LastAccess | NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                Filter = "*.*"
            };

            // Add event handlers for the DownloadDir watcher
            fileSystemWatcher.Changed += OnChanged;
            fileSystemWatcher.Created += OnChanged;
            fileSystemWatcher.Deleted += OnChanged;
            fileSystemWatcher.Renamed += OnChanged;

            fileSystemWatcher.EnableRaisingEvents = true;
            existingEntries = [];
        }

        private void OnChanged(object source, FileSystemEventArgs e)
        {
            RefreshItems();

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
                                Data.Add(new FileData(directoryName, directory)); // Assuming directories are songs
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
                                Data.Add(new FileData(displayName, file)); // Assuming files are not songs
                                existingEntries.Add(file);
                            }
                        }
                        break;
                }
            }
            else
            {
                _mainPage.DisplayAlert("Directory Not Found", $"The directory {DirectoryPath} does not exist.", "OK");
            }
        }

        public int GetItemCount()
        {
            if (Directory.Exists(DirectoryPath))
            {
                int totalFiles;

                switch (_watcherType)
                {
                    case WatcherType.Directory:
                        totalFiles = Directory.GetDirectories(DirectoryPath).Length;

                        return totalFiles;
                    case WatcherType.File:
                        totalFiles = Directory.GetFiles(DirectoryPath).Length;

                        return totalFiles;
                    default:
                        return 0;

                }
            }
            else
            {
                _mainPage.DisplayAlert("Directory Not Found", $"The directory {DirectoryPath} does not exist.", "OK");
                return 0;
            }
        }

        public void OpenLocation(int index)
        {
            var selectedItem = Data[index];
            string? directoryPath;

            try
            {
                switch (_watcherType)
                {
                    case WatcherType.Directory:
                        directoryPath = selectedItem.FilePath;
                        break;
                    case WatcherType.File:
                        directoryPath = Path.GetDirectoryName(selectedItem.FilePath);
                        break;
                    default:
                        directoryPath = selectedItem.FilePath;
                        break;
                }

                if (Directory.Exists(directoryPath))
                {
                    // Open the file location in the default file explorer
                    Process.Start("explorer.exe", directoryPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error opening file location: {ex.Message}");
            }
        }

        public async Task DeleteItem(int index)
        {

            var selectedItem = Data[index];

            if (Directory.Exists(DirectoryPath))
            {

                switch (_watcherType)
                {
                    case WatcherType.Directory:
                        if (selectedItem != null && Directory.Exists(selectedItem.FilePath))
                        {
                            var test = _mainPage.DisplayAlert("Confirmation", "Do you want to proceed?", "Yes", "No");

                            if (test.Result)
                            {
                                try
                                {
                                    Directory.Delete(selectedItem.FilePath, true);

                                    Data.RemoveAt(index);
                                    existingEntries.Remove(selectedItem.FilePath);
                                }
                                catch (Exception ex)
                                {
                                    await _mainPage.DisplayAlert("Error", $"Failed to delete the directory: {ex.Message}", "OK");
                                }
                            }
                        }
                        else
                        {
                            await _mainPage.DisplayAlert("Error", $"The directory: {selectedItem?.FilePath} does not exist", "OK");
                        }
                        break;
                    case WatcherType.File:
                        if (selectedItem != null && File.Exists(selectedItem.FilePath))
                        {
                            var test = _mainPage.DisplayAlert("Confirmation", "Do you want to proceed?", "Yes", "No");

                            if (test.Result)
                            {
                                try
                                {
                                    File.Delete(selectedItem.FilePath);
                                    Data.RemoveAt(index);
                                    existingEntries.Remove(selectedItem.FilePath);
                                }
                                catch (Exception ex)
                                {
                                    await _mainPage.DisplayAlert("Error", $"Failed to delete the file: {ex.Message}", "OK");
                                }
                            }
                        }
                        else
                        {
                            await _mainPage.DisplayAlert("Error", $"The file: {selectedItem?.FilePath} does not exist", "OK");
                        }
                        break;
                    default:
                        break;

                }
            }
            else
            {
                await _mainPage.DisplayAlert("Directory Not Found", $"The directory {DirectoryPath} does not exist.", "OK");
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
