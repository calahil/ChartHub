using System.Text;

namespace ChartHub.Server.Services;

/// <summary>Fields that may be patched in a song.ini file. Null = leave unchanged.</summary>
public sealed record SongIniPatchFields(
    string? Artist,
    string? Title,
    string? Charter,
    string? Genre,
    int? Year,
    int? DifficultyBand);

public interface ISongIniPatchService
{
    /// <summary>
    /// Patches the given song.ini file in place, updating only the provided fields.
    /// If the file does not exist a minimal [song] section is created.
    /// </summary>
    void PatchSongIni(string songIniPath, SongIniPatchFields fields);
}

public sealed class SongIniPatchService : ISongIniPatchService
{
    // Maps patch-field accessors onto their song.ini key names.
    private static readonly IReadOnlyList<(string Key, Func<SongIniPatchFields, string?> Get)> FieldMap =
        new List<(string, Func<SongIniPatchFields, string?>)>
        {
            ("name",       f => f.Title),
            ("artist",     f => f.Artist),
            ("charter",    f => f.Charter),
            ("genre",      f => f.Genre),
            ("year",       f => f.Year?.ToString()),
            ("diff_band",  f => f.DifficultyBand?.ToString()),
        };

    public void PatchSongIni(string songIniPath, SongIniPatchFields fields)
    {
        string[] originalLines = File.Exists(songIniPath)
            ? File.ReadAllLines(songIniPath)
            : Array.Empty<string>();

        var lines = new List<string>(originalLines);

        // Collect which keys need patching and what value to set.
        var patches = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, Func<SongIniPatchFields, string?> get) in FieldMap)
        {
            string? value = get(fields);
            if (value is not null)
            {
                patches[key] = value;
            }
        }

        if (patches.Count == 0)
        {
            return;
        }

        bool inSongSection = false;
        int songSectionEndIndex = -1; // index after last line of [song] section
        var updatedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < lines.Count; i++)
        {
            string trimmed = lines[i].Trim();

            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                if (inSongSection)
                {
                    // We've left the [song] section.
                    songSectionEndIndex = i;
                    inSongSection = false;
                }

                string section = trimmed[1..^1].Trim();
                if (section.Equals("song", StringComparison.OrdinalIgnoreCase))
                {
                    inSongSection = true;
                }

                continue;
            }

            if (!inSongSection)
            {
                continue;
            }

            int equalsIdx = trimmed.IndexOf('=');
            if (equalsIdx <= 0)
            {
                continue;
            }

            string key = trimmed[..equalsIdx].Trim();
            if (patches.TryGetValue(key, out string? newValue))
            {
                lines[i] = $"{key} = {Escape(newValue)}";
                updatedKeys.Add(key);
            }
        }

        // Handle remaining case: no [song] section at all.
        if (!inSongSection && songSectionEndIndex < 0 && updatedKeys.Count == 0)
        {
            // No [song] section found — create one.
            lines.Add("[song]");
            foreach ((string key, Func<SongIniPatchFields, string?> get) in FieldMap)
            {
                if (patches.TryGetValue(key, out string? value))
                {
                    lines.Add($"{key} = {Escape(value)}");
                    updatedKeys.Add(key);
                }
            }
        }
        else
        {
            // If [song] section hit EOF without another section, inSongSection is still true.
            if (inSongSection)
            {
                songSectionEndIndex = lines.Count;
            }

            // Append any keys not yet found.
            var keysToAppend = patches.Keys
                .Where(k => !updatedKeys.Contains(k))
                .ToList();

            if (keysToAppend.Count > 0)
            {
                int insertAt = songSectionEndIndex >= 0 ? songSectionEndIndex : lines.Count;
                int offset = 0;
                foreach (string key in keysToAppend)
                {
                    lines.Insert(insertAt + offset, $"{key} = {Escape(patches[key])}");
                    offset++;
                }
            }
        }

        File.WriteAllText(songIniPath, string.Join(Environment.NewLine, lines) + Environment.NewLine, Encoding.UTF8);
    }

    private static string Escape(string value) =>
        value.Replace('\n', ' ').Replace('\r', ' ');
}
