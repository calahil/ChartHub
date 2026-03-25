using ChartHub.BackupApi.Models;

namespace ChartHub.BackupApi.Services;

public interface IRhythmVerseUpstreamClient
{
    // updatedSince: Unix timestamp to pass as an upstream hint for incremental sync.
    // The exact parameter name and support depends on the upstream API; the value is appended
    // as query parameters but the caller must not rely on the upstream honouring it.
    Task<RhythmVersePageEnvelope> FetchSongsPageAsync(int page, int records, long? updatedSince, CancellationToken cancellationToken);

    IReadOnlyList<SyncedSong> ConvertToSyncedSongs(RhythmVersePageEnvelope envelope);
}
