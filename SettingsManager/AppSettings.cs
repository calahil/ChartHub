using System.Text.Json.Serialization;

namespace SettingsManager
{
    [method: JsonConstructor]
    public class AppSettings(
        string? nautilusDirectoryPath,
        string? phaseshiftDirectory,
        string? phaseshiftMusicDirectory,
        string? rhythmverseAppPath,
        string? cloneHeroSongLocation,
        string? downloadLocation,
        string? cloneHeroEXELocation,
        string? downloadStaging)
    {
        [JsonPropertyName("NautilusDirectoryPath")]
        public string? NautilusDirectoryPath { get; set; } = nautilusDirectoryPath;

        [JsonPropertyName("PhaseshiftDirectory")]
        public string? PhaseshiftDirectory { get; set; } = phaseshiftDirectory;

        [JsonPropertyName("PhaseshiftMusicDirectory")]
        public string? PhaseshiftMusicDirectory { get; set; } = phaseshiftMusicDirectory;

        [JsonPropertyName("RhythmverseAppPath")]
        public string? RhythmverseAppPath { get; set; } = rhythmverseAppPath;

        [JsonPropertyName("CloneHeroSongLocation")]
        public string? CloneHeroSongLocation { get; set; } = cloneHeroSongLocation;

        [JsonPropertyName("DownloadLocation")]
        public string? DownloadLocation { get; set; } = downloadLocation;

        [JsonPropertyName("CloneHeroEXELocation")]
        public string? CloneHeroEXELocation { get; set; } = cloneHeroEXELocation;

        [JsonPropertyName("DownloadStaging")]
        public string? DownloadStaging { get; set; } = downloadStaging;
    }
}