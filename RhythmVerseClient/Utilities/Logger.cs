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
}