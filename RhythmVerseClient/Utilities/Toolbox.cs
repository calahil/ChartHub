using Avalonia.Data.Converters;
using RhythmVerseClient.Models;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Archives.Zip;
using System.Globalization;

namespace RhythmVerseClient.Utilities
{
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

    }
}
