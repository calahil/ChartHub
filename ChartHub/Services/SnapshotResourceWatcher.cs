using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using Avalonia.Threading;

using ChartHub.Models;
using ChartHub.Utilities;

namespace ChartHub.Services;

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

    public void LoadItems()
    {
        _ = LoadItemsAsync();
    }

    private async Task LoadItemsAsync()
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
        foreach (string item in items)
        {
            try
            {
                string itemName = Path.GetFileName(item);
                WatcherFileType itemType = await WatcherFileTypeResolver.GetFileTypeAsync(item);
                string imageFile = WatcherFileTypeResolver.GetIconForFileType(itemType);
                long sizeBytes = _watcherType == WatcherType.Directory
                    ? FileTools.GetDirectorySize(item)
                    : new FileInfo(item).Length;

                files.Add(new WatcherFile(itemName, item, itemType, imageFile, sizeBytes));
            }
            catch (Exception ex)
            {
                Logger.LogError("Watcher", "Snapshot resource watcher skipped item after failure", ex, new Dictionary<string, object?>
                {
                    ["directoryPath"] = DirectoryPath,
                    ["itemPath"] = item,
                });
            }
        }

        Dispatcher.UIThread.Post(() => Data = new ObservableCollection<WatcherFile>(files));
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
