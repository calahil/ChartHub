namespace ChartHub.BackupApi.Options;

public sealed class RhythmVerseSourceOptions
{
    public const string SectionName = "RhythmVerseSource";

    public string BaseUrl { get; set; } = "https://rhythmverse.co/";

    public string SongsPath { get; set; } = "api/all/songfiles/search/live";

    public string Token { get; set; } = string.Empty;
}
