using ChartHub.Server.Contracts;
using ChartHub.Server.Options;

using Microsoft.Extensions.Options;

namespace ChartHub.Server.Services;

public interface ICloneHeroLibraryService
{
    IReadOnlyList<CloneHeroSongResponse> ListSongs();

    bool TryGetSong(string songId, out CloneHeroSongResponse? song);

    bool TrySoftDeleteSong(string songId, out CloneHeroSongResponse? song);

    bool TryRestoreSong(string songId, out CloneHeroSongResponse? song);
}

public sealed class CloneHeroLibraryService : ICloneHeroLibraryService
{
    private readonly string _cloneHeroRoot;
    private readonly Dictionary<string, CloneHeroSongResponse> _songs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CloneHeroSongResponse> _softDeletedSongs = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    public CloneHeroLibraryService(IOptions<ServerPathOptions> pathOptions)
    {
        string configuredRoot = pathOptions.Value.CloneHeroRoot;
        _cloneHeroRoot = Path.GetFullPath(configuredRoot);
        Directory.CreateDirectory(_cloneHeroRoot);

        var sample = new CloneHeroSongResponse
        {
            SongId = "sample-song-1",
            Artist = "Unknown Artist",
            Title = "Sample Song",
            Charter = "ChartHub",
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        _songs[sample.SongId] = sample;
    }

    public IReadOnlyList<CloneHeroSongResponse> ListSongs()
    {
        lock (_sync)
        {
            return _songs.Values
                .OrderByDescending(item => item.UpdatedAtUtc)
                .ToList();
        }
    }

    public bool TryGetSong(string songId, out CloneHeroSongResponse? song)
    {
        lock (_sync)
        {
            return _songs.TryGetValue(songId, out song);
        }
    }

    public bool TrySoftDeleteSong(string songId, out CloneHeroSongResponse? song)
    {
        lock (_sync)
        {
            if (!_songs.Remove(songId, out CloneHeroSongResponse? existing) || existing is null)
            {
                song = null;
                return false;
            }

            song = new CloneHeroSongResponse
            {
                SongId = existing.SongId,
                Artist = existing.Artist,
                Title = existing.Title,
                Charter = existing.Charter,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
            _softDeletedSongs[songId] = song;
            return true;
        }
    }

    public bool TryRestoreSong(string songId, out CloneHeroSongResponse? song)
    {
        lock (_sync)
        {
            if (!_softDeletedSongs.Remove(songId, out CloneHeroSongResponse? deleted) || deleted is null)
            {
                song = null;
                return false;
            }

            song = new CloneHeroSongResponse
            {
                SongId = deleted.SongId,
                Artist = deleted.Artist,
                Title = deleted.Title,
                Charter = deleted.Charter,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
            _songs[songId] = song;
            return true;
        }
    }

}
