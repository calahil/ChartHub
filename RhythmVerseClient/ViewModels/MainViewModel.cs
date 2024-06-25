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

        public MainViewModel(IFileSystemManager fileSystemManager)
        {
            FileManager = fileSystemManager;
            FileManager.AddWatcher(FileManager.CloneHeroSongsDir, WatcherType.Directory);
            FileManager.AddWatcher(FileManager.DownloadDir, WatcherType.File);
        }
    }
}
