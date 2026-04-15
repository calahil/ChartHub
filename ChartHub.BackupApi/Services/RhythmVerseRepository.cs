using System.Text.Json;
using System.Text.Json.Nodes;

using ChartHub.BackupApi.Models;
using ChartHub.BackupApi.Persistence;

using Microsoft.EntityFrameworkCore;

namespace ChartHub.BackupApi.Services;

public sealed partial class RhythmVerseRepository(
    BackupDbContext dbContext,
    ILogger<RhythmVerseRepository> logger) : IRhythmVerseRepository
{
    public async Task BeginReconciliationRunAsync(string reconciliationRunId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reconciliationRunId);

        string startedUtc = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

        await SetSyncStateValuesAsync(
            [
                new KeyValuePair<string, string>("reconciliation.current_run_id", reconciliationRunId),
                new KeyValuePair<string, string>("reconciliation.started_utc", startedUtc),
            ],
            cancellationToken).ConfigureAwait(false);
    }

    public async Task UpsertSongsAsync(
        IEnumerable<SyncedSong> songs,
        CancellationToken cancellationToken,
        string? reconciliationRunId = null)
    {
        var bufferedSongs = songs.ToList();
        if (bufferedSongs.Count == 0)
        {
            return;
        }

        var trackedSongsBySongId = dbContext.SongSnapshots.Local
            .GroupBy(x => x.SongId)
            .ToDictionary(x => x.Key, x => x.First());
        var trackedSongsByRecordId = dbContext.SongSnapshots.Local
            .Where(x => x.RecordId != null)
            .GroupBy(x => x.RecordId!, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);

        long[] songIds = bufferedSongs
            .Select(x => x.SongId)
            .Distinct()
            .ToArray();
        string[] recordIds = bufferedSongs
            .Select(x => x.RecordId)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        List<SongSnapshotEntity> existingSongs = await dbContext.SongSnapshots
            .Where(x => songIds.Contains(x.SongId) || (x.RecordId != null && recordIds.Contains(x.RecordId)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (SongSnapshotEntity existingSong in existingSongs)
        {
            trackedSongsBySongId.TryAdd(existingSong.SongId, existingSong);

            if (existingSong.RecordId != null)
            {
                trackedSongsByRecordId.TryAdd(existingSong.RecordId, existingSong);
            }
        }

        DateTimeOffset syncedUtc = DateTimeOffset.UtcNow;

        foreach (SyncedSong song in bufferedSongs)
        {
            SongSnapshotEntity? existing = null;

            if (trackedSongsBySongId.TryGetValue(song.SongId, out SongSnapshotEntity? existingBySongId))
            {
                if (TryGetConflictingRecordOwner(song.RecordId, existingBySongId, trackedSongsByRecordId, out SongSnapshotEntity? recordOwner))
                {
                    Log.RecordIdConflictIgnored(logger, song.RecordId!, song.SongId, recordOwner.SongId);
                    continue;
                }

                existing = existingBySongId;
            }
            else if (song.RecordId != null && trackedSongsByRecordId.TryGetValue(song.RecordId, out SongSnapshotEntity? existingByRecordId))
            {
                if (existingByRecordId.SongId != song.SongId)
                {
                    Log.RecordIdConflictIgnored(logger, song.RecordId, song.SongId, existingByRecordId.SongId);
                    continue;
                }

                existing = existingByRecordId;
            }

            if (existing is null)
            {
                existing = new SongSnapshotEntity
                {
                    SongId = song.SongId,
                };

                dbContext.SongSnapshots.Add(existing);
                trackedSongsBySongId[song.SongId] = existing;
            }

            ReindexRecordId(existing, song.RecordId, trackedSongsByRecordId);

            existing.RecordId = song.RecordId;
            existing.Artist = song.Artist;
            existing.Title = song.Title;
            existing.Album = song.Album;
            existing.Genre = song.Genre;
            existing.Year = song.Year;
            existing.RecordUpdatedUnix = song.RecordUpdatedUnix;
            existing.FileId = song.FileId;
            existing.DownloadUrl = song.DownloadUrl;
            existing.DiffGuitar = song.DiffGuitar;
            existing.DiffBass = song.DiffBass;
            existing.DiffDrums = song.DiffDrums;
            existing.DiffVocals = song.DiffVocals;
            existing.DiffKeys = song.DiffKeys;
            existing.DiffBand = song.DiffBand;
            existing.AuthorId = song.AuthorId;
            existing.GroupId = song.GroupId;
            existing.GameFormat = song.GameFormat;
            existing.SongJson = song.SongJson;
            existing.DataJson = song.DataJson;
            existing.FileJson = song.FileJson;
            existing.IsDeleted = false;
            existing.LastReconciledRunId = reconciliationRunId;
            existing.LastSyncedUtc = syncedUtc;

            trackedSongsBySongId[existing.SongId] = existing;
            if (song.RecordId != null)
            {
                trackedSongsByRecordId[song.RecordId] = existing;
            }
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            Log.UpsertBatchFailed(logger, bufferedSongs.Count, ex);
            dbContext.ChangeTracker.Clear();
            int savedCount = 0;
            int failedCount = 0;
            foreach (SyncedSong song in bufferedSongs)
            {
                try
                {
                    await UpsertSingleSongAsync(song, reconciliationRunId, syncedUtc, cancellationToken).ConfigureAwait(false);
                    savedCount++;
                }
                catch (DbUpdateException songEx)
                {
                    Log.UpsertSongFailed(logger, song.SongId, song.RecordId, songEx);
                    dbContext.ChangeTracker.Clear();
                    failedCount++;
                }
            }

            Log.UpsertFallbackComplete(logger, savedCount, failedCount);
        }
    }

    private async Task UpsertSingleSongAsync(
        SyncedSong song,
        string? reconciliationRunId,
        DateTimeOffset syncedUtc,
        CancellationToken cancellationToken)
    {
        SongSnapshotEntity? existing = await dbContext.SongSnapshots
            .FirstOrDefaultAsync(x => x.SongId == song.SongId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            existing = new SongSnapshotEntity { SongId = song.SongId };
            dbContext.SongSnapshots.Add(existing);
        }

        existing.RecordId = song.RecordId;
        existing.Artist = song.Artist;
        existing.Title = song.Title;
        existing.Album = song.Album;
        existing.Genre = song.Genre;
        existing.Year = song.Year;
        existing.RecordUpdatedUnix = song.RecordUpdatedUnix;
        existing.FileId = song.FileId;
        existing.DownloadUrl = song.DownloadUrl;
        existing.DiffGuitar = song.DiffGuitar;
        existing.DiffBass = song.DiffBass;
        existing.DiffDrums = song.DiffDrums;
        existing.DiffVocals = song.DiffVocals;
        existing.DiffKeys = song.DiffKeys;
        existing.DiffBand = song.DiffBand;
        existing.AuthorId = song.AuthorId;
        existing.GroupId = song.GroupId;
        existing.GameFormat = song.GameFormat;
        existing.SongJson = song.SongJson;
        existing.DataJson = song.DataJson;
        existing.FileJson = song.FileJson;
        existing.IsDeleted = false;
        existing.LastReconciledRunId = reconciliationRunId;
        existing.LastSyncedUtc = syncedUtc;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        dbContext.ChangeTracker.Clear();
    }

    public async Task FinalizeReconciliationRunAsync(string reconciliationRunId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reconciliationRunId);

        await dbContext.SongSnapshots
            .Where(x => !x.IsDeleted && x.LastReconciledRunId != reconciliationRunId)
            .ExecuteUpdateAsync(
                setter => setter.SetProperty(x => x.IsDeleted, true),
                cancellationToken)
            .ConfigureAwait(false);

        dbContext.ChangeTracker.Clear();

        string completedUtc = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

        await SetSyncStateValuesAsync(
            [
                new KeyValuePair<string, string>("reconciliation.current_run_id", reconciliationRunId),
                new KeyValuePair<string, string>("reconciliation.completed_utc", completedUtc),
            ],
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<RhythmVersePageEnvelope> GetSongsPageAsync(
        int page,
        int records,
        string? query,
        string? genre,
        string? gameformat,
        string? author,
        string? group,
        CancellationToken cancellationToken)
    {
        return await GetSongsPageAdvancedAsync(
            page,
            records,
            query,
            genre,
            gameformat,
            author,
            group,
            sortBy: null,
            sortOrder: null,
            instruments: null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<RhythmVersePageEnvelope> GetSongsPageAdvancedAsync(
        int page,
        int records,
        string? query,
        string? genre,
        string? gameformat,
        string? author,
        string? group,
        string? sortBy,
        string? sortOrder,
        IReadOnlyList<string>? instruments,
        CancellationToken cancellationToken)
    {
        int boundedPage = Math.Max(page, 1);
        int boundedRecords = Math.Clamp(records, 1, 250);
        IQueryable<SongSnapshotEntity> baseQuery = dbContext.SongSnapshots
            .AsNoTracking()
            .Where(x => !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(query))
        {
            string filter = query.Trim();
            string filterLower = filter.ToLowerInvariant();
            string filterNormalized = NormalizeSearchQuery(filter);

#pragma warning disable CA1862 // EF Core LINQ-to-SQL queries cannot translate StringComparison overloads; ToLower() is translatable to SQL and necessary here.
            // Always apply both predicates:
            //   1. case-insensitive raw match  → "Weird" finds "Weird Al Yankovic" on PostgreSQL
            //   2. punctuation-stripped match  → "ACDC" finds "AC/DC"; "Guns N Roses" finds "Guns N' Roses"
            baseQuery = baseQuery.Where(x =>
                x.Artist.ToLower().Contains(filterLower) ||
                x.Title.ToLower().Contains(filterLower) ||
                x.Album.ToLower().Contains(filterLower) ||
                x.Artist.Replace("/", "").Replace("-", "").Replace("'", "").ToLower().Contains(filterNormalized) ||
                x.Title.Replace("/", "").Replace("-", "").Replace("'", "").ToLower().Contains(filterNormalized) ||
                x.Album.Replace("/", "").Replace("-", "").Replace("'", "").ToLower().Contains(filterNormalized));
#pragma warning restore CA1862
        }

        if (!string.IsNullOrWhiteSpace(genre))
        {
            string genreFilter = genre.Trim();
            baseQuery = baseQuery.Where(x => x.Genre == genreFilter);
        }

        if (!string.IsNullOrWhiteSpace(gameformat))
        {
            string fmtFilter = gameformat.Trim();
            baseQuery = baseQuery.Where(x => x.GameFormat == fmtFilter);
        }

        if (!string.IsNullOrWhiteSpace(author))
        {
            string authorFilter = author.Trim();
            string authorFilterLower = authorFilter.ToLowerInvariant();
            string shortnameToken = string.Concat("\"shortname\":\"", authorFilter, "\"");
            string shortnameTokenLower = string.Concat("\"shortname\":\"", authorFilterLower, "\"");

#pragma warning disable CA1862 // EF Core LINQ-to-SQL queries cannot translate StringComparison overloads; ToLower() is translatable to SQL and necessary here.
            baseQuery = baseQuery.Where(x =>
                x.AuthorId == authorFilter
                || x.AuthorId.ToLower() == authorFilterLower
                || x.FileJson.Contains(shortnameToken)
                || x.FileJson.ToLower().Contains(shortnameTokenLower));
#pragma warning restore CA1862
        }

        if (!string.IsNullOrWhiteSpace(group))
        {
            string groupFilter = group.Trim();
            baseQuery = baseQuery.Where(x => x.GroupId == groupFilter);
        }

        HashSet<string> instrumentFamilies = BuildInstrumentFamilies(instruments);
        if (instrumentFamilies.Count > 0)
        {
            bool hasGuitar = instrumentFamilies.Contains("guitar");
            bool hasBass = instrumentFamilies.Contains("bass");
            bool hasDrums = instrumentFamilies.Contains("drums");
            bool hasVocals = instrumentFamilies.Contains("vocals");
            bool hasKeys = instrumentFamilies.Contains("keys");
            bool hasBand = instrumentFamilies.Contains("band");

            baseQuery = baseQuery.Where(x =>
                (hasGuitar && x.DiffGuitar != null)
                || (hasBass && x.DiffBass != null)
                || (hasDrums && x.DiffDrums != null)
                || (hasVocals && x.DiffVocals != null)
                || (hasKeys && x.DiffKeys != null)
                || (hasBand && x.DiffBand != null));
        }

        int totalFiltered = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);
        int totalAvailable = await dbContext.SongSnapshots
            .AsNoTracking()
            .CountAsync(x => !x.IsDeleted, cancellationToken)
            .ConfigureAwait(false);
        int start = (boundedPage - 1) * boundedRecords;

        IOrderedQueryable<SongSnapshotEntity> sortedQuery = ApplySort(baseQuery, sortBy, sortOrder);

        List<SongSnapshotEntity> songs = await sortedQuery
            .Skip(start)
            .Take(boundedRecords)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var payloadSongs = songs
            .Select(x => ParseSongPayload(x.SongJson, x.SongId, "GetSongsPageAsync"))
            .ToList();

        return new RhythmVersePageEnvelope
        {
            TotalAvailable = totalAvailable,
            TotalFiltered = totalFiltered,
            Returned = payloadSongs.Count,
            Start = start,
            Records = boundedRecords,
            Page = boundedPage,
            Songs = payloadSongs,
        };
    }

    public async Task<string?> GetDownloadUrlByFileIdAsync(string fileId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileId))
        {
            return null;
        }

        return await dbContext.SongSnapshots
            .AsNoTracking()
            .Where(x => x.FileId == fileId && !x.IsDeleted)
            .Select(x => x.DownloadUrl)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<JsonNode?> GetSongByIdAsync(long songId, CancellationToken cancellationToken)
    {
        string? json = await dbContext.SongSnapshots
            .AsNoTracking()
            .Where(x => x.SongId == songId && !x.IsDeleted)
            .Select(x => x.SongJson)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return json is null ? null : ParseSongPayload(json, songId, "GetSongByIdAsync");
    }

    public async Task SetSyncStateAsync(string key, string value, CancellationToken cancellationToken)
    {
        await SetSyncStateValuesAsync(
            [new KeyValuePair<string, string>(key, value)],
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetSyncStateAsync(string key, CancellationToken cancellationToken)
    {
        return await dbContext.SyncStates
            .AsNoTracking()
            .Where(x => x.Key == key)
            .Select(x => x.Value)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SetSyncStateValuesAsync(
        IReadOnlyList<KeyValuePair<string, string>> entries,
        CancellationToken cancellationToken)
    {
        if (entries.Count == 0)
        {
            return;
        }

        string[] keys = entries.Select(x => x.Key).Distinct(StringComparer.Ordinal).ToArray();
        Dictionary<string, SyncStateEntity> existingStates = await dbContext.SyncStates
            .Where(x => keys.Contains(x.Key))
            .ToDictionaryAsync(x => x.Key, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset updatedUtc = DateTimeOffset.UtcNow;

        foreach (KeyValuePair<string, string> entry in entries)
        {
            if (!existingStates.TryGetValue(entry.Key, out SyncStateEntity? state))
            {
                state = new SyncStateEntity
                {
                    Key = entry.Key,
                };

                dbContext.SyncStates.Add(state);
                existingStates[entry.Key] = state;
            }

            state.Value = entry.Value;
            state.UpdatedUtc = updatedUtc;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private JsonNode ParseSongPayload(string json, long songId, string operation)
    {
        try
        {
            var parsed = JsonNode.Parse(json);
            if (parsed is null)
            {
                throw new JsonException("Stored song payload resolved to null JSON.");
            }

            return parsed;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            Log.StoredSongPayloadInvalid(logger, songId, operation, ex);
            throw new StoredSongPayloadException(songId, operation, ex);
        }
    }

    private static bool TryGetConflictingRecordOwner(
        string? recordId,
        SongSnapshotEntity candidate,
        IReadOnlyDictionary<string, SongSnapshotEntity> trackedSongsByRecordId,
        out SongSnapshotEntity conflictingOwner)
    {
        if (recordId is null || !trackedSongsByRecordId.TryGetValue(recordId, out SongSnapshotEntity? existingByRecordId) || ReferenceEquals(existingByRecordId, candidate))
        {
            conflictingOwner = null!;
            return false;
        }

        conflictingOwner = existingByRecordId;
        return true;
    }

    private static void ReindexRecordId(
        SongSnapshotEntity existing,
        string? newRecordId,
        IDictionary<string, SongSnapshotEntity> trackedSongsByRecordId)
    {
        if (string.Equals(existing.RecordId, newRecordId, StringComparison.Ordinal))
        {
            return;
        }

        if (existing.RecordId != null
            && trackedSongsByRecordId.TryGetValue(existing.RecordId, out SongSnapshotEntity? currentOwner)
            && ReferenceEquals(currentOwner, existing))
        {
            trackedSongsByRecordId.Remove(existing.RecordId);
        }
    }

    private static HashSet<string> BuildInstrumentFamilies(IReadOnlyList<string>? instruments)
    {
        HashSet<string> families = new(StringComparer.Ordinal);
        if (instruments is null || instruments.Count == 0)
        {
            return families;
        }

        foreach (string instrument in instruments)
        {
            if (string.IsNullOrWhiteSpace(instrument))
            {
                continue;
            }

            string normalized = instrument.Trim().ToLowerInvariant();
            string? family = normalized switch
            {
                "guitar" or "guitarghl" or "proguitar" or "rhythm" or "guitarcoop" or "guitar_coop" => "guitar",
                "bass" or "bassghl" or "probass" => "bass",
                "drums" or "prodrums" => "drums",
                "vocals" => "vocals",
                "keys" or "prokeys" => "keys",
                "band" => "band",
                _ => null,
            };

            if (family is not null)
            {
                families.Add(family);
            }
        }

        return families;
    }

    /// <summary>
    /// Strips the most common connector-punctuation characters and lowercases the result so
    /// that a query without punctuation (e.g. "ACDC") can match a stored value that contains
    /// it (e.g. "AC/DC"), and vice versa.
    /// The same set of characters must be stripped from both the stored field (via EF
    /// Replace calls) and from the C#-side query before the Contains comparison is made.
    /// </summary>
    private static string NormalizeSearchQuery(string input)
    {
        // Strip: forward-slash, hyphen, apostrophe – the most common connector chars in
        // music artist/title names that users routinely omit when searching.
        System.Text.StringBuilder sb = new(input.Length);
        foreach (char c in input.ToLowerInvariant())
        {
            if (c is not '/' and not '-' and not '\'')
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Trim();
    }

    private static IOrderedQueryable<SongSnapshotEntity> ApplySort(
        IQueryable<SongSnapshotEntity> query,
        string? sortBy,
        string? sortOrder)
    {
        bool ascending = string.Equals(sortOrder?.Trim(), "asc", StringComparison.OrdinalIgnoreCase);
        string normalizedSort = sortBy?.Trim().ToLowerInvariant() ?? string.Empty;

        return normalizedSort switch
        {
            "artist" => ascending
                ? query.OrderBy(x => x.Artist).ThenBy(x => x.SongId)
                : query.OrderByDescending(x => x.Artist).ThenBy(x => x.SongId),
            "title" => ascending
                ? query.OrderBy(x => x.Title).ThenBy(x => x.SongId)
                : query.OrderByDescending(x => x.Title).ThenBy(x => x.SongId),
            "album" => ascending
                ? query.OrderBy(x => x.Album).ThenBy(x => x.SongId)
                : query.OrderByDescending(x => x.Album).ThenBy(x => x.SongId),
            "genre" => ascending
                ? query.OrderBy(x => x.Genre).ThenBy(x => x.SongId)
                : query.OrderByDescending(x => x.Genre).ThenBy(x => x.SongId),
            "year" => ascending
                ? query.OrderBy(x => x.Year).ThenBy(x => x.SongId)
                : query.OrderByDescending(x => x.Year).ThenBy(x => x.SongId),
            "author" => ascending
                ? query.OrderBy(x => x.AuthorId).ThenBy(x => x.SongId)
                : query.OrderByDescending(x => x.AuthorId).ThenBy(x => x.SongId),
            "gameformat" => ascending
                ? query.OrderBy(x => x.GameFormat).ThenBy(x => x.SongId)
                : query.OrderByDescending(x => x.GameFormat).ThenBy(x => x.SongId),
            "song_id" or "id" => ascending
                ? query.OrderBy(x => x.SongId)
                : query.OrderByDescending(x => x.SongId),
            "record_updated" or "release_date" or "downloads" or "song_length" or "length" or "updated" => ascending
                ? query.OrderBy(x => x.RecordUpdatedUnix).ThenBy(x => x.SongId)
                : query.OrderByDescending(x => x.RecordUpdatedUnix).ThenBy(x => x.SongId),
            _ => query.OrderByDescending(x => x.RecordUpdatedUnix).ThenBy(x => x.SongId),
        };
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1101, Level = LogLevel.Error, Message = "Stored RhythmVerse payload is invalid for SongId {SongId} during {Operation}.")]
        public static partial void StoredSongPayloadInvalid(ILogger logger, long songId, string operation, Exception exception);

        [LoggerMessage(EventId = 1102, Level = LogLevel.Warning, Message = "Skipping RhythmVerse song with RecordId {RecordId} because it conflicts with existing SongId {ExistingSongId}; incoming SongId was {IncomingSongId}.")]
        public static partial void RecordIdConflictIgnored(ILogger logger, string recordId, long incomingSongId, long existingSongId);

        [LoggerMessage(EventId = 1103, Level = LogLevel.Warning, Message = "Batch save failed for {Count} songs; falling back to per-song saves to isolate the problem.")]
        public static partial void UpsertBatchFailed(ILogger logger, int count, Exception exception);

        [LoggerMessage(EventId = 1104, Level = LogLevel.Warning, Message = "Per-song save failed for SongId {SongId} (RecordId: {RecordId}); song will be skipped.")]
        public static partial void UpsertSongFailed(ILogger logger, long songId, string? recordId, Exception exception);

        [LoggerMessage(EventId = 1105, Level = LogLevel.Information, Message = "Per-song fallback complete: {SavedCount} saved, {FailedCount} skipped.")]
        public static partial void UpsertFallbackComplete(ILogger logger, int savedCount, int failedCount);
    }
}
