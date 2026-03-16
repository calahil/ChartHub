using Avalonia.Data.Converters;
using RhythmVerseClient.Models;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Archives.Zip;
using System.Globalization;

namespace RhythmVerseClient.Utilities
{
    public static class FileTools
    {
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
                if (x is not WatcherFile fileDataX || y is not WatcherFile fileDataY)
                    return 0;

                return fileDataX.FileType.CompareTo(fileDataY.FileType);
            }
        }

        public class FileSizeComparer : IComparer<object>
        {
            public int Compare(object? x, object? y)
            {
                if (x is not WatcherFile fileDataX || y is not WatcherFile fileDataY)
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
}
