using System.Text.Json.Nodes;

using ChartHub.BackupApi.Models;
using ChartHub.BackupApi.Persistence;

using Microsoft.EntityFrameworkCore;

namespace ChartHub.BackupApi.Services;

public sealed class RhythmVerseRepository(BackupDbContext dbContext) : IRhythmVerseRepository
{
    public async Task UpsertSongsAsync(IEnumerable<SyncedSong> songs, CancellationToken cancellationToken)
    {
        foreach (SyncedSong song in songs)
        {
            SongSnapshotEntity? existing = await dbContext.SongSnapshots
                .FirstOrDefaultAsync(x => x.SongId == song.SongId, cancellationToken)
                .ConfigureAwait(false);

            if (existing is null)
            {
                existing = new SongSnapshotEntity
                {
                    SongId = song.SongId,
                };

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
            existing.LastSyncedUtc = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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
        int boundedPage = Math.Max(page, 1);
        int boundedRecords = Math.Clamp(records, 1, 250);
        IQueryable<SongSnapshotEntity> baseQuery = dbContext.SongSnapshots.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query))
        {
            string filter = query.Trim();
            baseQuery = baseQuery.Where(x =>
                x.Artist.Contains(filter) ||
                x.Title.Contains(filter) ||
                x.Album.Contains(filter));
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
            baseQuery = baseQuery.Where(x => x.AuthorId == authorFilter);
        }

        if (!string.IsNullOrWhiteSpace(group))
        {
            string groupFilter = group.Trim();
            baseQuery = baseQuery.Where(x => x.GroupId == groupFilter);
        }

        int totalFiltered = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);
        int totalAvailable = await dbContext.SongSnapshots.CountAsync(cancellationToken).ConfigureAwait(false);
        int start = (boundedPage - 1) * boundedRecords;

        List<SongSnapshotEntity> songs = await baseQuery
            .OrderByDescending(x => x.RecordUpdatedUnix)
            .ThenBy(x => x.SongId)
            .Skip(start)
            .Take(boundedRecords)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var payloadSongs = songs
            .Select(x => JsonNode.Parse(x.SongJson))
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
            .Where(x => x.FileId == fileId)
            .Select(x => x.DownloadUrl)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<JsonNode?> GetSongByIdAsync(long songId, CancellationToken cancellationToken)
    {
        string? json = await dbContext.SongSnapshots
            .AsNoTracking()
            .Where(x => x.SongId == songId)
            .Select(x => x.SongJson)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return json is null ? null : JsonNode.Parse(json);
    }

    public async Task SetSyncStateAsync(string key, string value, CancellationToken cancellationToken)
    {
        SyncStateEntity? state = await dbContext.SyncStates
            .FirstOrDefaultAsync(x => x.Key == key, cancellationToken)
            .ConfigureAwait(false);

        if (state is null)
        {
            state = new SyncStateEntity
            {
                Key = key,
            };

            dbContext.SyncStates.Add(state);
        }

        state.Value = value;
        state.UpdatedUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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
}
