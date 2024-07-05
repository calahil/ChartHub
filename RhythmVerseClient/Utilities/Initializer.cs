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
    public class Initializer
    {
        public const string ZIP_FILE_URL = "https://calahil.github.io/nautilus.zip";

        public static readonly string ZipFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nautilus.zip");

        public Initializer() { }

        private async void Initialize()
        {
            var NautilusDirectoryPath = Toolbox.ConstructPath(ApplicationData.Current.LocalFolder.Path, "nautilus");
            var nautilisEXE = Path.Combine(NautilusDirectoryPath, "Nautilus.exe");
            if (!File.Exists(nautilisEXE))
            {
                if (!File.Exists(ZipFilePath))
                {
                    HttpClient client = new();

                    byte[] data = await client.GetByteArrayAsync(ZIP_FILE_URL);
                    await File.WriteAllBytesAsync(ZipFilePath, data);

                    if (!Directory.Exists(NautilusDirectoryPath))
                    {
                        Directory.CreateDirectory(ApplicationData.Current.LocalFolder.Path);
                    }

                    using var archive = ArchiveFactory.Open(ZipFilePath);
                    foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
                    {
                        entry.WriteToDirectory(ApplicationData.Current.LocalFolder.Path, new ExtractionOptions
                        {
                            ExtractFullPath = true,
                            Overwrite = true
                        });
                    }
                }

                if (File.Exists(ZipFilePath) && File.Exists(nautilisEXE))
                {
                    File.Delete(ZipFilePath);
                }


                var PhaseshiftDir =Toolbox.ConstructPath(NautilusDirectoryPath)
                Toolbox.CreateDirectoryIfNotExists(PhaseshiftDir);
                Toolbox.CreateDirectoryIfNotExists(PhaseshiftMusicDir);
                Toolbox.CreateDirectoryIfNotExists(DownloadDir);
            }
        }

    }
}
