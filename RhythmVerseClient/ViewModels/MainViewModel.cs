using Microsoft.Extensions.FileSystemGlobbing;
using RhythmVerseClient.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RhythmVerseClient.ViewModels
{
    public class MainViewModel
    {
        public IFileSystemManager FileManager { get; }

        public ResourceWatcher DownloadWatcher { get; set; }
        public ResourceWatcher CloneHeroSongsWatcher {  get; set; }

        public MainViewModel(IFileSystemManager fileSystemManager)
        {
            FileManager = fileSystemManager;
            FileManager.Initialize();


            watcher.Initialize(path, watcherType);

            DownloadWatcher.LoadItems();
            CloneHeroSongsWatcher.LoadItems();
        }
    }
}
