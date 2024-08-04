using SharpCompress.Archives;
using SharpCompress.Common;
using Windows.Storage;

namespace RhythmVerseClient.Utilities
{
    public class Initializer
    {
        public const string ZIP_FILE_URL = "https://calahil.github.io/nautilus.zip";

        public static readonly string ZipFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nautilus.zip");

        public Initializer() { }

        public async Task InitializeAsync()
        {
            var NautilusDirectoryPath = Toolbox.ConstructPath(FileSystem.Current.AppDataDirectory, "nautilus");
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
                        Directory.CreateDirectory(FileSystem.Current.AppDataDirectory);
                    }

                    using var archive = ArchiveFactory.Open(ZipFilePath);
                    foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
                    {
                        entry.WriteToDirectory(FileSystem.Current.AppDataDirectory, new ExtractionOptions
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


                var PhaseshiftDir = Toolbox.ConstructPath(NautilusDirectoryPath, "phaseshift");
                var PhaseshiftMusicDir = Toolbox.ConstructPath(PhaseshiftDir, "Music");
                var DownloadDir = Toolbox.ConstructPath(PhaseshiftDir, "downloads");
                var DownloadStaging = Toolbox.ConstructPath(PhaseshiftDir, "staging");
                var CloneHeroDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Clone Hero");
                var CloneHeroSongsDir = Toolbox.ConstructPath(CloneHeroDataDir, "Songs");
                Toolbox.CreateDirectoryIfNotExists(PhaseshiftDir);
                Toolbox.CreateDirectoryIfNotExists(PhaseshiftMusicDir);
                Toolbox.CreateDirectoryIfNotExists(DownloadDir);
                Toolbox.CreateDirectoryIfNotExists(DownloadStaging);
                Toolbox.CreateDirectoryIfNotExists(CloneHeroDataDir);
                Toolbox.CreateDirectoryIfNotExists(CloneHeroSongsDir);
            }
        }

    }
}
