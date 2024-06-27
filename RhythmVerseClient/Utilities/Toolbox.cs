using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RhythmVerseClient.Utilities
{
    public class Toolbox
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

    }
}
