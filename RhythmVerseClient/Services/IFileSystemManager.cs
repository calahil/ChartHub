using System.Collections.ObjectModel;

namespace RhythmVerseClient.Services
{
    public interface IFileSystemManager
    {
        ObservableCollection<ResourceWatcher> ResourceWatchers { get; }
        void AddWatcher(string path, WatcherType watcherType);
        void RemoveWatcher(string path);
    }

}
