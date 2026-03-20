namespace ChartHub.Services;

public sealed record SongMetadata(string Artist, string Title, string Charter)
{
    public static readonly SongMetadata Unknown = new("Unknown Artist", "Unknown Song", "Unknown Charter");
}
