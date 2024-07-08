using System.Collections.ObjectModel;

namespace RhythmVerseClient.Services
{
    public interface IResourceWatcher
    {
        string DirectoryPath { get; }
        ObservableCollection<FileData> Data { get; set; }
        void LoadItems();
        event EventHandler<string> DirectoryNotFound;
        event EventHandler<string> ErrorOccurred;
    }

}
