namespace ChartHub.Configuration.Models;

public enum RhythmVerseSource
{
    RhythmVerseOfficial,
    ChartHubMirror,
}

public static class RhythmVerseSourceUrls
{
    public static Uri GetBaseUri(RhythmVerseSource source)
    {
        return source switch
        {
            RhythmVerseSource.ChartHubMirror => new Uri("http://127.0.0.1:5147"),
            _ => new Uri("https://rhythmverse.co"),
        };
    }
}
