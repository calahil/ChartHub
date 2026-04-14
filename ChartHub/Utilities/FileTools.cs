using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Archives.Zip;

namespace ChartHub.Utilities;

public static class FileTools
{
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
        string extension = Path.GetExtension(filePath).ToLower();
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
        foreach (string file in Directory.GetFiles(source))
        {
            string destFile = Path.Combine(destination, Path.GetFileName(file));
            System.IO.File.Copy(file, destFile, true); // true to overwrite
            System.IO.File.Delete(file); // Delete the original file
        }

        // Recursively move subdirectories
        foreach (string directory in Directory.GetDirectories(source))
        {
            string destDir = Path.Combine(destination, Path.GetFileName(directory));
            MoveDirectory(directory, destDir); // Recursive call
            Directory.Delete(directory);
        }
    }
}
