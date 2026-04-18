namespace ChartHub.Server.Contracts;

public sealed class CloneHeroSongResponse
{
    public required string SongId { get; init; }

    public required string Source { get; init; }

    public required string SourceId { get; init; }

    public required string Artist { get; init; }

    public required string Title { get; init; }

    public required string Charter { get; init; }

    public string? SourceMd5 { get; init; }

    public string? SourceChartHash { get; init; }

    public string? SourceUrl { get; init; }

    public string? InstalledPath { get; init; }

    public string? InstalledRelativePath { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }
}

/// <summary>Partial update request for song.ini metadata. All fields are optional.</summary>
public sealed class SongIniPatchRequest
{
    public string? Artist { get; init; }

    public string? Title { get; init; }

    public string? Charter { get; init; }

    public string? Genre { get; init; }

    public int? Year { get; init; }

    /// <summary>Overall band difficulty (-1 = unset, 0–6 = Easy…Expert+).</summary>
    public int? DifficultyBand { get; init; }
}
