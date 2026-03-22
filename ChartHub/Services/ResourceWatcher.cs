using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using Avalonia.Threading;

using ChartHub.Models;
using ChartHub.Utilities;

namespace ChartHub.Services;

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

    private readonly FileSystemWatcher _fileSystemWatcher;
    private readonly HashSet<string> _existingEntries;
    private readonly WatcherType _watcherType;

    public event EventHandler<string>? DirectoryNotFound;

    public ResourceWatcher(string path, WatcherType watcherType)
    {
        _directoryPath = path;
        _watcherType = watcherType;
        _data = [];
        _fileSystemWatcher = new FileSystemWatcher();
        _existingEntries = [];
        _fileSystemWatcher.Path = DirectoryPath;
        _fileSystemWatcher.NotifyFilter = NotifyFilters.LastAccess | NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName;
        _fileSystemWatcher.Filter = "*.*";
        _fileSystemWatcher.Created += OnCreated;
        _fileSystemWatcher.Deleted += OnDeleted;
        _fileSystemWatcher.Renamed += OnRenamed;
        _fileSystemWatcher.EnableRaisingEvents = true;
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

            foreach (string item in items)
            {
                if (!_existingEntries.Contains(item))
                {
                    string itemName = Path.GetFileName(item);
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
            string itemName = e.Name ?? Path.GetFileName(e.FullPath);
            _ = UpdateItem(itemName, e.FullPath, e.OldFullPath);
        });
    }

    private void OnDeleted(object sender, FileSystemEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                string itemName = e.Name ?? Path.GetFileName(e.FullPath);
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
            string itemName = e.Name ?? Path.GetFileName(e.FullPath);
            await AddItem(itemName, e.FullPath);
        });
    }

    private static async Task<WatcherFileType> GetFileTypeAsync(string filePath)
    {
        return await WatcherFileTypeResolver.GetFileTypeAsync(filePath);
    }

    private static string GetIconForFileType(WatcherFileType fileType)
    {
        return WatcherFileTypeResolver.GetIconForFileType(fileType);
    }

    private async Task AddItem(string itemName, string itemPath)
    {
        if (!_existingEntries.Contains(itemPath))
        {
            WatcherFileType fileType = await GetFileTypeAsync(itemPath);
            string imageFile = GetIconForFileType(fileType);
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
                if (!_existingEntries.Contains(itemPath))
                {
                    Data.Add(new WatcherFile(itemName, itemPath, fileType, imageFile, sizeBytes));
                    _existingEntries.Add(itemPath);
                }
            });
        }
    }

    private void DeleteItem(string itemName, string itemPath)
    {
        WatcherFile? itemToRemove = Data.FirstOrDefault(file => file.DisplayName == itemName);

        if (itemToRemove != null)
        {
            Data.Remove(itemToRemove);
        }

        if (_existingEntries.Contains(itemPath))
        {
            _existingEntries.Remove(itemPath);
        }
    }

    private async Task UpdateItem(string itemName, string itemPath, string oldItemPath)
    {
        try
        {
            WatcherFileType fileType = await GetFileTypeAsync(itemPath).ConfigureAwait(false);
            string imageFile = GetIconForFileType(fileType);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                WatcherFile? itemToEdit = Data.FirstOrDefault(item => item.FilePath == oldItemPath);

                if (itemToEdit != null)
                {
                    itemToEdit.FileType = fileType;
                    itemToEdit.ImageFile = imageFile;
                    itemToEdit.FilePath = itemPath;
                    itemToEdit.DisplayName = itemName;
                }

                if (_existingEntries.Contains(oldItemPath))
                {
                    _existingEntries.Remove(oldItemPath);
                }

                _existingEntries.Add(itemPath);
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
        _fileSystemWatcher.EnableRaisingEvents = false;
        _fileSystemWatcher.Created -= OnCreated;
        _fileSystemWatcher.Deleted -= OnDeleted;
        _fileSystemWatcher.Renamed -= OnRenamed;
        _fileSystemWatcher.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
