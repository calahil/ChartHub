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
            RhythmVerseSource.ChartHubMirror => new Uri("https://rhythmverse.calahilstudios.com"),
            _ => new Uri("https://rhythmverse.co"),
        };
    }
}
