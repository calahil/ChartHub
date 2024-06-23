using SettingsManager;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RhythmVerseClient.Services
{
    public class FileSystemManager
    {
        // Phaseshift Path (Where Nautilus looks for rb3con files by default)
        public string PhaseshiftDir { get; set; }

        // Phaseshift Music Path (Where Nautilus writes the converted files to)
        public string PhaseshiftMusicDir { get; set; }

        // The App's Root Path
        public string RhythmverseAppPath { get; set; }

        public string DownloadDir { get; set; }

        public string CloneHeroSongsDir { get; set; }

        private SettingsManager<AppSettings> _settingsManager;
        private MainPage _mainPage;

        public ObservableCollection<ResourceWatcher> ResourceWatcher { get; set; } = [];


        private FileSystemManager(SettingsManager<AppSettings> settingsManager, MainPage mainPage)
        {
            RhythmverseAppPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "rhythmverse");
            string baseDir = Path.Combine(RhythmverseAppPath, "nautilus");
            PhaseshiftDir = ConstructPath(baseDir, "phaseshift");
            PhaseshiftMusicDir = ConstructPath(PhaseshiftDir, "Music");

            _settingsManager = settingsManager;
            _mainPage = mainPage;
            DownloadDir = _settingsManager.Get("DownloadLocation");
            CloneHeroSongsDir = _settingsManager.Get("CloneHeroSongLocation");
            ResourceWatcher.Add(new(DownloadDir, WatcherType.File, _mainPage));
            ResourceWatcher.Add(new(CloneHeroSongsDir, WatcherType.Directory, _mainPage));

        }

        // Helper to build paths
        private static string ConstructPath(params string[] pathSegments)
        {
            return Path.Combine(pathSegments);
        }

        public void Initialize()
        {
            CreateDirectoryIfNotExists(PhaseshiftMusicDir);
        }

        private static void CreateDirectoryIfNotExists(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
        }

        public void DebugResetSongProcessor()
        {
            foreach (var file in Directory.GetFiles(PhaseshiftDir))
            {
                string destFile = Path.Combine(DownloadDir, Path.GetFileName(file));
                File.Copy(file, destFile, true); // true to overwrite
                File.Delete(file); // Delete the original file
            }

            foreach (string directory in Directory.GetDirectories(PhaseshiftMusicDir))
            {
                foreach (string file in Directory.GetFiles(directory))
                {
                    File.Delete(file); // Delete the original file
                }
                Directory.Delete(directory);
            }
        }
        /* public void MoveDirectory(string source, string destination)
         {
             // Create the destination directory if it doesn't exist
             Directory.CreateDirectory(destination);

             // Move each file and overwrite if the file already exists
             foreach (var file in Directory.GetFiles(source))
             {
                 string destFile = Path.Combine(destination, Path.GetFileName(file));
                 File.Copy(file, destFile, true); // true to overwrite
                 File.Delete(file); // Delete the original file
             }

             // Recursively move subdirectories
             foreach (var directory in Directory.GetDirectories(source))
             {
                 string destDir = Path.Combine(destination, Path.GetFileName(directory));
                 MoveDirectory(directory, destDir); // Recursive call
             }

             // Delete the source directory now that it's empty
             Directory.Delete(source);
         }*/
    }
}
