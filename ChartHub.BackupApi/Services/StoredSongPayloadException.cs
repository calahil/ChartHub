namespace ChartHub.BackupApi.Services;

public sealed class StoredSongPayloadException : InvalidOperationException
{
    public StoredSongPayloadException(long songId, string operation, Exception innerException)
        : base($"Stored RhythmVerse song payload is invalid for SongId {songId} during {operation}.", innerException)
    {
        SongId = songId;
        Operation = operation;
    }

    public long SongId { get; }

    public string Operation { get; }
}