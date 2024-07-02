using SettingsManager;
using SharpCompress.Archives;
using SharpCompress.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;

namespace RhythmVerseClient.Utilities
{
    public static class Toolbox
    {
        public static string ConvertFileSize(long sizeBytes)
        {
            string[] sizeSuffixes = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };
            if (sizeBytes == 0)
            {
                return "0B";
            }

            int order = (int)Math.Log(sizeBytes, 1024);
            double adjustedSize = sizeBytes / Math.Pow(1024, order);
            return $"{Math.Round(adjustedSize, 2)} {sizeSuffixes[order]}";
        }

        public static long GetDirectorySize(string folderPath)
        {
            DirectoryInfo di = new DirectoryInfo(folderPath);
            return di.EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
        }

        public static string ConstructPath(params string[] pathSegments)
        {
            return Path.Combine(pathSegments);
        }

        public static void CreateDirectoryIfNotExists(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
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

    public class AppGlobalSettings
    {
        private readonly ISettingsManager<AppSettings> _settingsManager;
        private readonly AppSettings _appSettings;

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

        public AppGlobalSettings(ISettingsManager<AppSettings> settingsManager)
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
                PhaseshiftDir = Toolbox.ConstructPath(NautilusDirectoryPath, "phaseshift");
            }

            if (PhaseshiftMusicDir == "first_install")
            {

                PhaseshiftMusicDir = Toolbox.ConstructPath(PhaseshiftDir, "Music");
            }

            if (DownloadDir == "first_install")
            {
                DownloadDir = Toolbox.ConstructPath(PhaseshiftDir, "downloads"); ;
            }

            if (CloneHeroSongsDir == "first_install")
            {
                var cloneHeroDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Clone Hero");
                CloneHeroSongsDir = Toolbox.ConstructPath(cloneHeroDataDir, "Songs");
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
    }
}
