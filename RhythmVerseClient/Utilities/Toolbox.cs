using Avalonia.Data.Converters;
using RhythmVerseClient.Models;
using SettingsManager;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Archives.Zip;
using System.Globalization;

namespace RhythmVerseClient.Utilities
{
    public static class Logger
    {
        private static readonly string LogFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "errorlog.txt");

        public static void LogError(Exception ex)
        {
            try
            {
                using StreamWriter writer = new(LogFilePath, true);
                writer.WriteLine($"[{DateTime.Now}] {ex.GetType()}: {ex.Message}");
                writer.WriteLine(ex.StackTrace);
                writer.WriteLine();
            }
            catch
            {
                // Handle any exceptions that occur while trying to write to the log file
            }
        }

        public static void LogMessage(string message)
        {
            try
            {
                using StreamWriter writer = new(LogFilePath, true);
                writer.WriteLine($"[{DateTime.Now}] {message}");
            }
            catch
            {
                // Handle any exceptions that occur while trying to write to the log file
            }
        }
    }

    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool booleanValue)
            {
                return !booleanValue;
            }
            return false;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool booleanValue)
            {
                return !booleanValue;
            }
            return false;
        }
    }

    public static class Toolbox
    {
        public static string ConvertFilter(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }
            return input.Replace("Song ", "").ToLower();
        }

        public static string ConvertSecondstoText(long? input)
        {
            if (input != null)
            {
                long? minutes = input / 60;
                int seconds = (int)input % 60;


                return $"{minutes}:{seconds:D2}";
            }
            else
            {
                return "00:00";
            }
        }

        public static string GetSortOrder(string filter, string order)
        {
            bool isStringField = filter.Equals("Artist", StringComparison.OrdinalIgnoreCase) ||
                                 filter.Equals("Title", StringComparison.OrdinalIgnoreCase);

            // Adjust order based on the type of data
            if (isStringField)
            {
                return order == "Ascending" ? "ASC" : "DESC";
            }
            else // Assume numerical data for other fields
            {
                return order == "Ascending" ? "DESC" : "ASC";
            }
        }

        public static string ConvertFileSize(long sizeBytes)
        {
            string[] sizeSuffixes = ["B", "KB", "MB", "GB", "TB", "PB", "EB"];
            if (sizeBytes == 0)
            {
                return "0B";
            }

            int order = (int)Math.Log(sizeBytes, 1024);
            double adjustedSize = sizeBytes / Math.Pow(1024, order);
            return $"{Math.Round(adjustedSize, 2)} {sizeSuffixes[order]}";
        }

        public class FileTypeComparer : IComparer<object>
        {
            public int Compare(object? x, object? y)
            {
                if (x is not FileData fileDataX || y is not FileData fileDataY)
                    return 0;

                return fileDataX.FileType.CompareTo(fileDataY.FileType);
            }
        }

        public class FileSizeComparer : IComparer<object>
        {
            public int Compare(object? x, object? y)
            {
                if (x is not FileData fileDataX || y is not FileData fileDataY)
                    return 0;

                return fileDataX.SizeBytes.CompareTo(fileDataY.SizeBytes);
            }
        }

        public static long GetDirectorySize(string folderPath)
        {
            DirectoryInfo di = new(folderPath);
            return di.EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
        }

        public static void CreateDirectoryIfNotExists(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
        }

        public static IArchive OpenArchive(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLower();
            return extension switch
            {
                ".zip" => ZipArchive.Open(filePath),
                ".rar" => RarArchive.Open(filePath),
                ".7z" => SevenZipArchive.Open(filePath),
                _ => throw new NotSupportedException($"File type {extension} is not supported.")
            };
        }

        public static void DebugResetSongProcessor(string phaseshiftDir, string downloadDir, string phaseshiftMusicDir)
        {
            foreach (var file in Directory.GetFiles(phaseshiftDir))
            {
                string destFile = Path.Combine(downloadDir, Path.GetFileName(file));
                System.IO.File.Copy(file, destFile, true); // true to overwrite
                System.IO.File.Delete(file); // Delete the original file
            }

            foreach (string directory in Directory.GetDirectories(phaseshiftMusicDir))
            {
                foreach (string file in Directory.GetFiles(directory))
                {
                    System.IO.File.Delete(file); // Delete the original file
                }
                Directory.Delete(directory);
            }
        }

        public static void MoveDirectory(string source, string destination)
        {
            // Create the destination directory if it doesn't exist
            Directory.CreateDirectory(destination);

            // Move each file and overwrite if the file already exists
            foreach (var file in Directory.GetFiles(source))
            {
                string destFile = Path.Combine(destination, Path.GetFileName(file));
                System.IO.File.Copy(file, destFile, true); // true to overwrite
                System.IO.File.Delete(file); // Delete the original file
            }

            // Recursively move subdirectories
            foreach (var directory in Directory.GetDirectories(source))
            {
                string destDir = Path.Combine(destination, Path.GetFileName(directory));
                MoveDirectory(directory, destDir); // Recursive call
                Directory.Delete(directory);
            }
        }
    }

    public class AppGlobalSettings
    {
        private readonly ISettingsManager<AppSettings> _settingsManager;
        private readonly AppSettings _appSettings;

        public bool UseMockData
        {
            get => _appSettings.UseMockData;
            set { _appSettings.UseMockData = value; _settingsManager.Save(); }
        }
        public string TempDir
        {
            get => _appSettings.TempDirectory ?? String.Empty;
            set { _appSettings.TempDirectory = value; _settingsManager.Save(); }
        }

        public string StagingDir
        {
            get => _appSettings.StagingDirectory ?? String.Empty;
            set { _appSettings.StagingDirectory = value; _settingsManager.Save(); }
        }

        public string DownloadDir
        {
            get => _appSettings.DownloadDirectory ?? String.Empty;
            set { _appSettings.DownloadDirectory = value; _settingsManager.Save(); }
        }

        public string OutputDir
        {
            get => _appSettings.OutputDirectory ?? String.Empty;
            set { _appSettings.OutputDirectory = value; _settingsManager.Save(); }
        }

        public string CloneHeroDataDir
        {
            get => _appSettings.CloneHeroDataDirectory ?? String.Empty;
            set { _appSettings.CloneHeroDataDirectory = value; _settingsManager.Save(); }
        }

        public string CloneHeroSongsDir
        {
            get => _appSettings.CloneHeroSongDirectory ?? String.Empty;
            set { _appSettings.CloneHeroSongDirectory = value; _settingsManager.Save(); }
        }

        public AppGlobalSettings(ISettingsManager<AppSettings> settingsManager)
        {
            _settingsManager = settingsManager;

            _appSettings = settingsManager.Settings;

            if (TempDir == "first_install" || TempDir == String.Empty)
            {
                TempDir = Path.Combine(Path.GetTempPath(), "RhythmVerseClient");
            }

            Toolbox.CreateDirectoryIfNotExists(TempDir);

            if (DownloadDir == "first_install" || DownloadDir == String.Empty)
            {
                DownloadDir = Path.Combine(TempDir, "Downloads");
            }

            Toolbox.CreateDirectoryIfNotExists(DownloadDir);

            if (StagingDir == "first_install" || StagingDir == String.Empty)
            {
                StagingDir = Path.Combine(TempDir, "Staging");
            }

            Toolbox.CreateDirectoryIfNotExists(StagingDir);

            if (OutputDir == "first_install" || OutputDir == String.Empty)
            {
                OutputDir = Path.Combine(TempDir, "Output");
            }

            Toolbox.CreateDirectoryIfNotExists(OutputDir);

            if (CloneHeroSongsDir == "first_install" || CloneHeroSongsDir == String.Empty)
            {
                CloneHeroSongsDir = Path.Combine(CloneHeroDataDir, "Songs");
            }
            Toolbox.CreateDirectoryIfNotExists(CloneHeroSongsDir);
        }


    }
}
