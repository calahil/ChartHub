using ChartHub.Models;
using ChartHub.Utilities;

namespace ChartHub.Services
{
    internal static class WatcherFileTypeResolver
    {
        private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];
        private static readonly byte[] RarSignature = "Rar!"u8.ToArray();
        private static readonly byte[] Rb3ConSignature = "CON"u8.ToArray();
        private static readonly byte[] SevenZipSignature = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];

        internal static async Task<WatcherFileType> GetFileTypeAsync(string filePath)
        {
            if (Directory.Exists(filePath))
                return WatcherFileType.CloneHero;

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (extension == ".zip")
                return WatcherFileType.Zip;
            if (extension == ".rar")
                return WatcherFileType.Rar;
            if (extension == ".7z")
                return WatcherFileType.SevenZip;

            byte[] fileSignature = new byte[6];

            try
            {
                using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                _ = await fs.ReadAsync(fileSignature);
            }
            catch (UnauthorizedAccessException)
            {
                return WatcherFileType.Unknown;
            }
            catch (Exception ex)
            {
                if (Directory.Exists(filePath))
                    return WatcherFileType.CloneHero;

                Logger.LogError("Watcher", "Resource watcher failed to read file metadata", ex, new Dictionary<string, object?>
                {
                    ["filePath"] = filePath,
                });
                return WatcherFileType.Unknown;
            }

            if (fileSignature.Length >= ZipSignature.Length && fileSignature.AsSpan()[..ZipSignature.Length].SequenceEqual(ZipSignature))
                return WatcherFileType.Zip;
            if (fileSignature.Length >= RarSignature.Length && fileSignature.AsSpan()[..RarSignature.Length].SequenceEqual(RarSignature))
                return WatcherFileType.Rar;
            if (fileSignature.Length >= Rb3ConSignature.Length && fileSignature.AsSpan()[..Rb3ConSignature.Length].SequenceEqual(Rb3ConSignature))
                return WatcherFileType.Con;
            if (fileSignature.Length >= SevenZipSignature.Length && fileSignature.AsSpan()[..SevenZipSignature.Length].SequenceEqual(SevenZipSignature))
                return WatcherFileType.SevenZip;

            return WatcherFileType.Unknown;
        }

        internal static string GetIconForFileType(WatcherFileType fileType)
        {
            var iconFileName = fileType switch
            {
                WatcherFileType.Rar => "rar.png",
                WatcherFileType.Zip => "zip.png",
                WatcherFileType.Con => "rb.png",
                WatcherFileType.SevenZip => "sevenzip.png",
                WatcherFileType.CloneHero => "clonehero.png",
                _ => "blank.png",
            };

            return $"avares://ChartHub/Resources/Images/{iconFileName}";
        }
    }
}