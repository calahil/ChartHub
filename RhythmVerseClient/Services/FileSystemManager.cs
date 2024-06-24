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
        public string PhaseshiftDir { get; set; }

        public string PhaseshiftMusicDir { get; set; }

        public string RhythmverseAppPath { get; set; }

        public string DownloadDir { get; set; }

        public string CloneHeroSongsDir { get; set; }

        private SettingsManager<AppSettings> _settingsManager;

        public const string ZIP_FILE_URL = "https://calahil.github.io/nautilus.zip";
        public static readonly string NautilusDirectoryPath = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RhythmVerseClient"), "nautilus");
        public static readonly string ZipFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nautilus.zip");

        public ObservableCollection<ResourceWatcher> ResourceWatchers { get; set; } = [];


        public FileSystemManager(SettingsManager<AppSettings> settingsManager)
        {
            RhythmverseAppPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RhythmVerseClient");
            string baseDir = Path.Combine(RhythmverseAppPath, "nautilus");
            PhaseshiftDir = ConstructPath(baseDir, "phaseshift");
            PhaseshiftMusicDir = ConstructPath(PhaseshiftDir, "Music");

            _settingsManager = settingsManager;
            DownloadDir = _settingsManager.Get("DownloadLocation");
            CloneHeroSongsDir = _settingsManager.Get("CloneHeroSongLocation");
            Initialize();
        }

        public void AddWatcher(string path, WatcherType watcherType)
        {
            var watcher = new ResourceWatcher();
            watcher.Initialize(path, watcherType);
            watcher.DirectoryNotFound += (sender, message) => OnDirectoryNotFound(message);
            watcher.ErrorOccurred += (sender, message) => OnErrorOccurred(message);

            ResourceWatchers.Add(watcher);
        }

        public void RemoveWatcher(string path)
        {
            var watcher = ResourceWatchers.FirstOrDefault(w => w.DirectoryPath == path);
            if (watcher != null)
            {
                ResourceWatchers.Remove(watcher);
            }
        }

        private void OnDirectoryNotFound(string directoryPath)
        {
            // Handle directory not found scenario
        }

        private void OnErrorOccurred(string errorMessage)
        {
            // Handle errors
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
