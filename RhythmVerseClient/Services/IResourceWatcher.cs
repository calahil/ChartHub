using System.Collections.ObjectModel;

namespace RhythmVerseClient.Services
{
    public interface IResourceWatcher
    {
        string DirectoryPath { get; }
        ObservableCollection<FileData> Data { get; }
        void Initialize(string path, WatcherType watcherType);
        event EventHandler<string> DirectoryNotFound;
        event EventHandler<string> ErrorOccurred;
    }

}
