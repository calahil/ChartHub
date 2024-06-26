using SettingsManager;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Xml.Dom;
using Windows.Storage;

namespace RhythmVerseClient.Services
{
    public class FileSystemManager : IFileSystemManager
    {
        public string PhaseshiftDir
        {
            get => _appSettings.PhaseshiftDirectory ?? String.Empty;
            set { _appSettings.PhaseshiftDirectory = value; _settingsManager.Save(); }
        }

        public string NautilusDirectoryPath
        {
            get => _appSettings.NautilusDirectoryPath ?? String.Empty;
            set { _appSettings.NautilusDirectoryPath = value; _settingsManager.Save(); }
        }

        public string PhaseshiftMusicDir
        {
            get => _appSettings.PhaseshiftMusicDirectory ?? String.Empty;
            set { _appSettings.PhaseshiftMusicDirectory = value; _settingsManager.Save(); }
        }

        public string RhythmverseAppPath
        {
            get => _appSettings.RhythmverseAppPath ?? String.Empty;
            set { _appSettings.RhythmverseAppPath = value; _settingsManager.Save(); }
        }

        public string DownloadDir
        {
            get => _appSettings.DownloadLocation ?? String.Empty;
            set { _appSettings.DownloadLocation = value; _settingsManager.Save(); }
        }

        public string CloneHeroSongsDir
        {
            get => _appSettings.CloneHeroSongLocation ?? String.Empty;
            set { _appSettings.CloneHeroSongLocation = value; _settingsManager.Save(); }
        }

        private ISettingsManager<AppSettings> _settingsManager;
        private AppSettings _appSettings;

        public const string ZIP_FILE_URL = "https://calahil.github.io/nautilus.zip";

        public static readonly string ZipFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nautilus.zip");

        public ObservableCollection<ResourceWatcher> ResourceWatchers { get; set; } = [];


        public FileSystemManager(ISettingsManager<AppSettings> settingsManager)
        {
            _settingsManager = settingsManager;

            _appSettings = settingsManager.Settings;

            if (RhythmverseAppPath == "first_install")
            {
                RhythmverseAppPath = ApplicationData.Current.LocalFolder.Path;
            }

            if (NautilusDirectoryPath == "first_install")
            {
                NautilusDirectoryPath = Path.Combine(RhythmverseAppPath, "nautilus");
            }

            if (PhaseshiftDir == "first_install")
            {
                PhaseshiftDir = ConstructPath(NautilusDirectoryPath, "phaseshift");
            }

            if (PhaseshiftMusicDir == "first_install")
            {

                PhaseshiftMusicDir = ConstructPath(PhaseshiftDir, "Music");
            }

            if (DownloadDir == "first_install")
            {
                DownloadDir = "C:\\";
            }

            if (CloneHeroSongsDir == "first_install")
            {
                CloneHeroSongsDir = "C:\\";
            }
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
