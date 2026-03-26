using System.ComponentModel.DataAnnotations;

namespace ChartHub.BackupApi.Persistence;

public sealed class SongSnapshotEntity
{
    [Key]
    public long SongId { get; set; }

    [MaxLength(256)]
    public string? RecordId { get; set; }

    [MaxLength(512)]
    public string Artist { get; set; } = string.Empty;

    [MaxLength(512)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(512)]
    public string Album { get; set; } = string.Empty;

    [MaxLength(128)]
    public string Genre { get; set; } = string.Empty;

    public int? Year { get; set; }

    public long? RecordUpdatedUnix { get; set; }

    [MaxLength(128)]
    public string FileId { get; set; } = string.Empty;

    [MaxLength(2048)]
    public string DownloadUrl { get; set; } = string.Empty;

    public int? DiffGuitar { get; set; }

    public int? DiffBass { get; set; }

    public int? DiffDrums { get; set; }

    public int? DiffVocals { get; set; }

    public int? DiffKeys { get; set; }

    public int? DiffBand { get; set; }

    [MaxLength(128)]
    public string AuthorId { get; set; } = string.Empty;

    [MaxLength(64)]
    public string GroupId { get; set; } = string.Empty;

    [MaxLength(64)]
    public string GameFormat { get; set; } = string.Empty;

    public string SongJson { get; set; } = "{}";

    public string DataJson { get; set; } = "{}";

    public string FileJson { get; set; } = "{}";

    public bool IsDeleted { get; set; }

    [MaxLength(64)]
    public string? LastReconciledRunId { get; set; }

    public DateTimeOffset LastSyncedUtc { get; set; }
}
