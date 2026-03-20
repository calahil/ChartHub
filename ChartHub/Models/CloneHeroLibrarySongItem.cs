namespace ChartHub.Models;

public sealed class CloneHeroLibrarySongItem
{
    public string Artist { get; init; } = "Unknown Artist";

    public string Title { get; init; } = "Unknown Song";

    public string Charter { get; init; } = "Unknown Charter";

    public string Source { get; init; } = string.Empty;

    public string SourceId { get; init; } = string.Empty;

    public string LocalPath { get; init; } = string.Empty;

    public string SongIniPath => Path.Combine(LocalPath, "song.ini");
}
