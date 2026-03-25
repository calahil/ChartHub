using ChartHub.BackupApi.Models;

namespace ChartHub.BackupApi.Services;

public interface IRhythmVerseRepository
{
    Task UpsertSongsAsync(IEnumerable<SyncedSong> songs, CancellationToken cancellationToken);

    Task<RhythmVersePageEnvelope> GetSongsPageAsync(
        int page,
        int records,
        string? query,
        string? genre,
        string? gameformat,
        string? author,
        string? group,
        CancellationToken cancellationToken);

    Task<string?> GetDownloadUrlByFileIdAsync(string fileId, CancellationToken cancellationToken);

    Task<System.Text.Json.Nodes.JsonNode?> GetSongByIdAsync(long songId, CancellationToken cancellationToken);

    Task SetSyncStateAsync(string key, string value, CancellationToken cancellationToken);

    Task<string?> GetSyncStateAsync(string key, CancellationToken cancellationToken);
}
