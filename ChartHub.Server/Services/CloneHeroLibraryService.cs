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

    bool TryInstallFromStaged(Guid jobId, string displayName, string stagedPath, out CloneHeroSongResponse? song, out string? installedPath);
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

    public bool TryInstallFromStaged(Guid jobId, string displayName, string stagedPath, out CloneHeroSongResponse? song, out string? installedPath)
    {
        song = null;
        installedPath = null;

        if (string.IsNullOrWhiteSpace(stagedPath))
        {
            return false;
        }

        string resolvedStagedPath = Path.GetFullPath(stagedPath);
        if (!File.Exists(resolvedStagedPath) && !Directory.Exists(resolvedStagedPath))
        {
            return false;
        }

        string sanitizedBaseName = SanitizeSongId(displayName);
        string songId = $"{sanitizedBaseName}-{jobId:D}";
        string destination = Path.Combine(_cloneHeroRoot, songId);
        Directory.CreateDirectory(destination);

        if (Directory.Exists(resolvedStagedPath))
        {
            CopyDirectoryContents(resolvedStagedPath, destination);
        }
        else
        {
            string extension = Path.GetExtension(resolvedStagedPath);
            if (string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase))
            {
                System.IO.Compression.ZipFile.ExtractToDirectory(resolvedStagedPath, destination, overwriteFiles: true);
            }
            else
            {
                string fileDestination = Path.Combine(destination, Path.GetFileName(resolvedStagedPath));
                File.Copy(resolvedStagedPath, fileDestination, overwrite: true);
            }
        }

        song = BuildSongRecord(songId, displayName);
        installedPath = destination;

        lock (_sync)
        {
            _softDeletedSongs.Remove(songId);
            _songs[songId] = song;
        }

        return true;
    }

    private static CloneHeroSongResponse BuildSongRecord(string songId, string displayName)
    {
        string artist = "Unknown Artist";
        string title = string.IsNullOrWhiteSpace(displayName) ? "Unknown Song" : displayName.Trim();

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            string[] split = displayName.Split(" - ", count: 2, StringSplitOptions.TrimEntries);
            if (split.Length == 2)
            {
                artist = string.IsNullOrWhiteSpace(split[0]) ? artist : split[0];
                title = string.IsNullOrWhiteSpace(split[1]) ? title : split[1];
            }
        }

        return new CloneHeroSongResponse
        {
            SongId = songId,
            Artist = artist,
            Title = title,
            Charter = "ChartHub.Server",
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static string SanitizeSongId(string displayName)
    {
        string fallback = "clonehero-song";
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return fallback;
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(displayName.Length);
        foreach (char c in displayName)
        {
            if (invalid.Contains(c) || char.IsControl(c))
            {
                builder.Append('-');
            }
            else
            {
                builder.Append(c);
            }
        }

        string normalized = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static void CopyDirectoryContents(string sourceDirectory, string destinationDirectory)
    {
        foreach (string directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
        }

        foreach (string file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, file);
            string destinationPath = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, overwrite: true);
        }
    }
}
