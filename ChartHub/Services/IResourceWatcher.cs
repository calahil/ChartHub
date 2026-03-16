using System.Collections.ObjectModel;
using ChartHub.Models;

namespace ChartHub.Services
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
