using System.Text;

using ChartHub.Conversion.Models;

namespace ChartHub.Conversion.Sng;

/// <summary>
/// Extracts song metadata from a parsed SNGPKG container.
/// </summary>
internal static class SngMetadataExtractor
{
    private const string UnknownArtist = "Unknown Artist";
    private const string UnknownCharter = "Unknown Charter";

    internal static ConversionMetadata Extract(SngPackage package, byte[] containerBytes, string sourcePath)
    {
        string fallbackTitle = Path.GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(fallbackTitle))
        {
            fallbackTitle = "Unknown Song";
        }

        string title = fallbackTitle;
        string artist = UnknownArtist;
        string charter = UnknownCharter;

        if (SngPackageReader.TryFindEntry(package, "song.ini", out SngFileEntry? songIniEntry)
            && songIniEntry != null)
        {
            byte[] iniBytes = SngPackageReader.ReadFileData(containerBytes, songIniEntry);
            string iniText = DecodeText(iniBytes);
            (string? parsedTitle, string? parsedArtist, string? parsedCharter) = ParseSongIni(iniText);

            if (!string.IsNullOrWhiteSpace(parsedTitle))
            {
                title = parsedTitle.Trim();
            }

            if (!string.IsNullOrWhiteSpace(parsedArtist))
            {
                artist = parsedArtist.Trim();
            }

            if (!string.IsNullOrWhiteSpace(parsedCharter))
            {
                charter = parsedCharter.Trim();
            }
        }

        return new ConversionMetadata(artist, title, charter);
    }

    private static string DecodeText(byte[] data)
    {
        // song.ini payloads may include UTF-8 BOM; TrimStart removes it after decode.
        string text = Encoding.UTF8.GetString(data);
        return text.TrimStart('\uFEFF');
    }

    private static (string? Title, string? Artist, string? Charter) ParseSongIni(string content)
    {
        string? title = null;
        string? artist = null;
        string? charter = null;
        bool inSongSection = false;

        string[] lines = content.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed[0] == ';')
            {
                continue;
            }

            if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']')
            {
                string section = trimmed[1..^1].Trim();
                inSongSection = section.Equals("song", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inSongSection)
            {
                continue;
            }

            int equalsIndex = trimmed.IndexOf('=');
            if (equalsIndex <= 0)
            {
                continue;
            }

            string key = trimmed[..equalsIndex].Trim();
            string value = trimmed[(equalsIndex + 1)..].Trim();

            if (key.Equals("name", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
            {
                title = value;
            }
            else if (key.Equals("artist", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
            {
                artist = value;
            }
            else if (key.Equals("charter", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
            {
                charter = value;
            }
        }

        return (title, artist, charter);
    }
}
