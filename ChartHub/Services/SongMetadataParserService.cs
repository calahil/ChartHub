using System;
using System.IO;
using System.Threading.Tasks;

namespace ChartHub.Services;

/// <summary>
/// Resilient parser for Clone Hero song.ini metadata files.
/// 
/// Parsing Rules:
/// - Section names are case-insensitive ([song], [Song], etc.)
/// - Keys are case-insensitive and trimmed
/// - Looks for: name (title), artist, charter
/// - Ignores comment lines (// and ;) and malformed lines
/// - Fallback values: Unknown Artist, Unknown Song, Unknown Charter
/// 
/// This parser prioritizes robustness: it will survive malformed lines
/// instead of failing, allowing installs to proceed even with imperfect metadata.
/// </summary>
public class SongMetadataParserService
{
    /// <summary>
    /// Parse metadata from a song.ini file path.
    /// Returns metadata with fallback values if file missing or unparseable.
    /// </summary>
    public async Task<SongMetadata> ParseAsync(string iniFilePath)
    {
        if (!File.Exists(iniFilePath))
        {
            return SongMetadata.Unknown;
        }

        try
        {
            var lines = await File.ReadAllLinesAsync(iniFilePath);
            return ParseLines(lines);
        }
        catch
        {
            // Survive file read errors; return metadata with fallbacks
            return SongMetadata.Unknown;
        }
    }

    /// <summary>
    /// Parse metadata from in-memory file content (lines).
    /// Used for testing and when lines are already available.
    /// </summary>
    public SongMetadata Parse(string[] lines)
    {
        return ParseLines(lines);
    }

    /// <summary>
    /// Parse metadata from a string containing ini file content.
    /// </summary>
    public SongMetadata ParseContent(string content)
    {
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        return Parse(lines);
    }

    private SongMetadata ParseLines(string[] lines)
    {
        string? artist = null;
        string? title = null;
        string? charter = null;
        bool inSongSection = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Skip empty lines
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            // Skip comment lines (// and ;)
            if (trimmed.StartsWith("//") || trimmed.StartsWith(";"))
            {
                continue;
            }

            // Check for section header [song] (case-insensitive)
            if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
            {
                var sectionName = trimmed.Substring(1, trimmed.Length - 2).Trim();
                inSongSection = sectionName.Equals("song", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            // Only process key=value pairs if we're in the [song] section
            if (!inSongSection)
            {
                continue;
            }

            // Parse key=value line
            var equalsIndex = trimmed.IndexOf('=');
            if (equalsIndex <= 0)
            {
                // Malformed line; skip it
                continue;
            }

            var key = trimmed.Substring(0, equalsIndex).Trim();
            var value = trimmed.Substring(equalsIndex + 1).Trim();

            // Extract metadata based on key (case-insensitive)
            if (key.Equals("name", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
            {
                title = value;
            }
            else if (key.Equals("artist", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
            {
                artist = value;
            }
            else if (key.Equals("charter", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
            {
                charter = value;
            }
            // Unknown keys are silently ignored
        }

        // Build result with fallbacks
        var finalArtist = !string.IsNullOrWhiteSpace(artist) ? artist.Trim() : "Unknown Artist";
        var finalTitle = !string.IsNullOrWhiteSpace(title) ? title.Trim() : "Unknown Song";
        var finalCharter = !string.IsNullOrWhiteSpace(charter) ? charter.Trim() : "Unknown Charter";

        return new SongMetadata(finalArtist, finalTitle, finalCharter);
    }
}
