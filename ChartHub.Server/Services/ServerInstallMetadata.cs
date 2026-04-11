namespace ChartHub.Server.Services;

public sealed record ServerSongMetadata(string Artist, string Title, string Charter)
{
    public static readonly ServerSongMetadata Unknown = new("Unknown Artist", "Unknown Song", "Unknown Charter");
}

public interface IServerSongIniMetadataParser
{
    ServerSongMetadata ParseFromSongIni(string songIniPath);
}

public sealed class ServerSongIniMetadataParser : IServerSongIniMetadataParser
{
    public ServerSongMetadata ParseFromSongIni(string songIniPath)
    {
        if (string.IsNullOrWhiteSpace(songIniPath) || !File.Exists(songIniPath))
        {
            return ServerSongMetadata.Unknown;
        }

        bool inSongSection = false;
        string? artist = null;
        string? title = null;
        string? charter = null;
        string? frets = null;

        foreach (string rawLine in File.ReadLines(songIniPath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith(';'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                string sectionName = line[1..^1].Trim();
                inSongSection = sectionName.Equals("song", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inSongSection)
            {
                continue;
            }

            int splitIndex = line.IndexOf('=');
            if (splitIndex <= 0)
            {
                continue;
            }

            string key = line[..splitIndex].Trim();
            string value = line[(splitIndex + 1)..].Trim();
            if (value.Length == 0)
            {
                continue;
            }

            if (key.Equals("artist", StringComparison.OrdinalIgnoreCase))
            {
                artist = value;
            }
            else if (key.Equals("name", StringComparison.OrdinalIgnoreCase))
            {
                title = value;
            }
            else if (key.Equals("charter", StringComparison.OrdinalIgnoreCase))
            {
                charter = value;
            }
            else if (key.Equals("frets", StringComparison.OrdinalIgnoreCase))
            {
                frets = value;
            }
        }

        return new ServerSongMetadata(
            Artist: Normalize(artist, "Unknown Artist"),
            Title: Normalize(title, "Unknown Song"),
            Charter: Normalize(charter ?? frets, "Unknown Charter"));
    }

    private static string Normalize(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
    }
}

public sealed record ServerCloneHeroDirectoryLayout(
    string ArtistSegment,
    string SongSegment,
    string CharterSourceSegment,
    string RelativePath,
    string FullPath);

public interface IServerCloneHeroDirectorySchemaService
{
    string NormalizeSource(string? source);

    ServerCloneHeroDirectoryLayout ResolveUniqueLayout(
        string cloneHeroSongsRoot,
        ServerSongMetadata metadata,
        string? source,
        Func<string, bool>? exists = null);
}

public sealed class ServerCloneHeroDirectorySchemaService : IServerCloneHeroDirectorySchemaService
{
    public string NormalizeSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return "unknown";
        }

        string normalized = source.Trim().ToLowerInvariant();
        return normalized switch
        {
            "rhythmverse" => "rhythmverse",
            "encore" => "encore",
            _ => normalized,
        };
    }

    public ServerCloneHeroDirectoryLayout ResolveUniqueLayout(
        string cloneHeroSongsRoot,
        ServerSongMetadata metadata,
        string? source,
        Func<string, bool>? exists = null)
    {
        exists ??= Directory.Exists;

        string artistSegment = ServerSafePathHelper.SanitizeFileName(metadata.Artist, "Unknown Artist");
        string songSegment = ServerSafePathHelper.SanitizeFileName(metadata.Title, "Unknown Song");
        string charterSegment = ServerSafePathHelper.SanitizeFileName(metadata.Charter, "Unknown Charter");
        string sourceSegment = NormalizeSource(source);
        string leafSegment = $"{charterSegment}__{sourceSegment}";

        string artistPath = Path.Combine(cloneHeroSongsRoot, artistSegment);
        string songPath = Path.Combine(artistPath, songSegment);
        string finalPath = Path.Combine(songPath, leafSegment);

        if (!exists(finalPath))
        {
            return new ServerCloneHeroDirectoryLayout(
                ArtistSegment: artistSegment,
                SongSegment: songSegment,
                CharterSourceSegment: leafSegment,
                RelativePath: Path.Combine(artistSegment, songSegment, leafSegment),
                FullPath: finalPath);
        }

        int counter = 2;
        while (true)
        {
            string candidateLeaf = $"{leafSegment}_{counter}";
            string candidate = Path.Combine(songPath, candidateLeaf);
            if (!exists(candidate))
            {
                return new ServerCloneHeroDirectoryLayout(
                    ArtistSegment: artistSegment,
                    SongSegment: songSegment,
                    CharterSourceSegment: candidateLeaf,
                    RelativePath: Path.Combine(artistSegment, songSegment, candidateLeaf),
                    FullPath: candidate);
            }

            counter++;
        }
    }
}
