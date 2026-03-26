namespace ChartHub.BackupApi.Models;

public sealed class SyncedSong
{
    public required long SongId { get; init; }

    public required string? RecordId { get; init; }

    public string Artist { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Album { get; init; } = string.Empty;

    public string Genre { get; init; } = string.Empty;

    public int? Year { get; init; }

    public long? RecordUpdatedUnix { get; init; }

    public string FileId { get; init; } = string.Empty;

    public string DownloadUrl { get; init; } = string.Empty;

    public int? DiffGuitar { get; init; }

    public int? DiffBass { get; init; }

    public int? DiffDrums { get; init; }

    public int? DiffVocals { get; init; }

    public int? DiffKeys { get; init; }

    public int? DiffBand { get; init; }

    public string AuthorId { get; init; } = string.Empty;

    public string GroupId { get; init; } = string.Empty;

    public string GameFormat { get; init; } = string.Empty;

    public required string SongJson { get; init; }

    public required string DataJson { get; init; }

    public required string FileJson { get; init; }
}
