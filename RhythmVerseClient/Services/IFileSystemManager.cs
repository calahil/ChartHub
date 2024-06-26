using SettingsManager;
using System.Collections.ObjectModel;

namespace RhythmVerseClient.Services
{
    public interface IFileSystemManager
    {
        ObservableCollection<ResourceWatcher> ResourceWatchers { get; }
        string PhaseshiftDir {  get; set; }
        string PhaseshiftMusicDir { get; set; }
        string RhythmverseAppPath { get; set; }
        string NautilusDirectoryPath { get; set; }
        string DownloadDir { get; set; }
        string CloneHeroSongsDir { get; set; }

        void AddWatchers(int count);
        void RemoveWatcher(string path);
        ResourceWatcher GetDownloadWatcher();
        ResourceWatcher GetCloneHeroSongWatcher();
        Task Initialize();
    }


}
