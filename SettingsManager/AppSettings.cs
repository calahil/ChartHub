using System.Text.Json.Serialization;

namespace SettingsManager
{
    [method: JsonConstructor]
    public class AppSettings(
        string? tempDirectory,
        string? downloadDirectory,
        string? cloneHeroSongDirectory,
        string? cloneHeroDataDirectory)
    {
        [JsonPropertyName("TempDirectory")]
        public string? TempDirectory { get; set; } = tempDirectory;

        [JsonPropertyName("DownloadDirectory")]
        public string? DownloadDirectory { get; set; } = downloadDirectory;

        [JsonPropertyName("CloneHeroSongDirectory")]
        public string? CloneHeroSongDirectory { get; set; } = cloneHeroSongDirectory;

        [JsonPropertyName("CloneHeroDataDirectory")]
        public string? CloneHeroDataDirectory { get; set; } = cloneHeroDataDirectory;
    }
}