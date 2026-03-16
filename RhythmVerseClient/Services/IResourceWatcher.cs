using System.Collections.ObjectModel;
using RhythmVerseClient.Models;

namespace RhythmVerseClient.Services
{
    public interface IResourceWatcher
    {
        string DirectoryPath { get; }
        ObservableCollection<WatcherFile> Data { get; set; }
        void LoadItems();
        event EventHandler<string> DirectoryNotFound;
        //event EventHandler<string> ErrorOccurred;
    }
}
