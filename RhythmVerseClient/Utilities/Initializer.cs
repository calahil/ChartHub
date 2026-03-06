namespace RhythmVerseClient.Utilities
{
    public class Initializer
    {
        public Initializer() { }

        public static async Task InitializeAsync()
        {
            var TempDir = Path.Combine(Path.GetTempPath(), "RhythmVerseClient");
            var DownloadDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var CloneHeroDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".clonehero");
            var CloneHeroSongsDir = Path.Combine(CloneHeroDataDir, "Songs");
        }

    }
}
