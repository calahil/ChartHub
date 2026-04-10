namespace ChartHub.Server.Contracts;

public sealed class CloneHeroSongResponse
{
    public required string SongId { get; init; }

    public required string Artist { get; init; }

    public required string Title { get; init; }

    public required string Charter { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }
}
