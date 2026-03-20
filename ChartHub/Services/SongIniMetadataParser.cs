namespace ChartHub.Services;

public interface ISongIniMetadataParser
{
    SongMetadata ParseFromSongIni(string songIniPath);
}

public sealed class SongIniMetadataParser : ISongIniMetadataParser
{
    public SongMetadata ParseFromSongIni(string songIniPath)
    {
        if (string.IsNullOrWhiteSpace(songIniPath) || !File.Exists(songIniPath))
            return SongMetadata.Unknown;

        var inSongSection = false;
        string? artist = null;
        string? title = null;
        string? charter = null;
        string? frets = null;

        foreach (var rawLine in File.ReadLines(songIniPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith(';'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var sectionName = line[1..^1].Trim();
                inSongSection = sectionName.Equals("song", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inSongSection)
                continue;

            var splitIndex = line.IndexOf('=');
            if (splitIndex <= 0)
                continue;

            var key = line[..splitIndex].Trim();
            var value = line[(splitIndex + 1)..].Trim();
            if (value.Length == 0)
                continue;

            if (key.Equals("artist", StringComparison.OrdinalIgnoreCase))
                artist = value;
            else if (key.Equals("name", StringComparison.OrdinalIgnoreCase))
                title = value;
            else if (key.Equals("charter", StringComparison.OrdinalIgnoreCase))
                charter = value;
            else if (key.Equals("frets", StringComparison.OrdinalIgnoreCase))
                frets = value;
        }

        return new SongMetadata(
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
