using System.Text;

using ChartHub.Conversion.Dta;

namespace ChartHub.Conversion.SongIni;

/// <summary>
/// Generates a Clone Hero–compatible <c>song.ini</c> file from Rock Band DTA metadata.
/// </summary>
internal static class SongIniGenerator
{
    private static readonly int[] DrumsDiffMap = [124, 151, 178, 242, 345, 448];
    private static readonly int[] VocalDiffMap = [132, 175, 218, 279, 353, 427];
    private static readonly int[] BassDiffMap = [135, 181, 228, 293, 364, 436];
    private static readonly int[] GuitarDiffMap = [139, 176, 221, 267, 333, 409];
    private static readonly int[] KeysDiffMap = [153, 211, 269, 327, 385, 443];
    private static readonly int[] BandDiffMap = [163, 215, 267, 321, 375, 429];
    private static readonly IReadOnlyDictionary<string, string> GenreDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["indierock"] = "Indie Rock",
    };

    /// <summary>Generates <c>song.ini</c> content from DTA song info.</summary>
    public static string Generate(DtaSongInfo info)
    {
        var sb = new StringBuilder();
        sb.Append("[song]\r\n");

        string diffBand = ToDifficulty(info.Ranks, "band", BandDiffMap);
        string diffGuitar = ToDifficulty(info.Ranks, "guitar", GuitarDiffMap);
        string diffBass = ToDifficulty(info.Ranks, "bass", BassDiffMap);
        string diffDrums = ToDifficulty(info.Ranks, "drum", DrumsDiffMap, "drums");
        string diffKeys = ToDifficulty(info.Ranks, "keys", KeysDiffMap);
        string diffVocals = ToDifficulty(info.Ranks, "vocals", VocalDiffMap, "vocal");
        string diffVocalsHarm = info.VocalParts > 1 ? diffVocals : "-1";
        bool hasDrums = HasRank(info.Ranks, "drum", "drums");

        AppendPair(sb, "name", info.Title);
        AppendPair(sb, "artist", info.Artist);
        AppendPair(sb, "album", info.Album);
        AppendPair(sb, "charter", info.Charter);
        AppendPair(sb, "frets", info.Charter);
        AppendPair(sb, "year", info.Year);
        AppendPair(sb, "genre", FormatGenre(info.Genre));
        AppendShown(sb, "pro_drums", hasDrums ? true : null);
        AppendPair(sb, "song_length", info.SongLengthMs > 0 ? info.SongLengthMs.ToString() : null);
        AppendPair(sb, "preview_start_time", info.PreviewStartMs > 0 ? info.PreviewStartMs.ToString() : null);
        AppendPair(sb, "preview_end_time", info.PreviewEndMs > 0 ? info.PreviewEndMs.ToString() : null);
        AppendPair(sb, "diff_band", diffBand);
        AppendPair(sb, "diff_guitar", diffGuitar);
        AppendPair(sb, "diff_guitarghl", "-1");
        AppendPair(sb, "diff_bass", diffBass);
        AppendPair(sb, "diff_bassghl", "-1");
        AppendPair(sb, "diff_drums", diffDrums);
        AppendPair(sb, "diff_drums_real", hasDrums ? diffDrums : "-1");
        AppendPair(sb, "diff_keys", diffKeys);
        AppendPair(sb, "diff_keys_real", "-1");
        AppendPair(sb, "diff_vocals", diffVocals);
        AppendPair(sb, "diff_vocals_harm", diffVocalsHarm);
        AppendPair(sb, "diff_dance", "-1");
        AppendPair(sb, "diff_bass_real", "-1");
        AppendPair(sb, "diff_guitar_real", "-1");
        AppendPair(sb, "diff_bass_real_22", null);
        AppendPair(sb, "diff_guitar_real_22", null);
        AppendPair(sb, "diff_guitar_coop", "-1");
        AppendPair(sb, "diff_rhythm", ToDifficulty(info.Ranks, "rhythm", BassDiffMap));
        AppendPair(sb, "diff_drums_real_ps", "-1");
        AppendPair(sb, "diff_keys_real_ps", "-1");
        AppendPair(sb, "diff_guitar_pad", "-1");
        AppendPair(sb, "diff_bass_pad", "-1");
        AppendPair(sb, "diff_drums_pad", "-1");
        AppendPair(sb, "diff_vocals_pad", "-1");
        AppendPair(sb, "diff_keys_pad", "-1");
        AppendPair(sb, "star_power_note", "116");
        AppendPair(sb, "multiplier_note", "116");
        AppendPair(sb, "track", info.AlbumTrack > 0 ? info.AlbumTrack.ToString() : null);
        AppendPair(sb, "album_track", info.AlbumTrack > 0 ? info.AlbumTrack.ToString() : null);
        AppendShown(sb, "sysex_slider", false);
        AppendShown(sb, "sysex_open_bass", false);
        AppendShown(sb, "five_lane_drums", null);
        AppendShown(sb, "drum_fallback_blue", null);
        return sb.ToString();
    }

    private static string Escape(string value)
    {
        // song.ini does not support multi-line values; replace control characters.
        return value.Replace('\n', ' ').Replace('\r', ' ');
    }

    private static void AppendPair(StringBuilder sb, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        sb.Append(key);
        sb.Append(" = ");
        sb.Append(Escape(value));
        sb.Append("\r\n");
    }

    private static void AppendShown(StringBuilder sb, string key, bool? value)
    {
        if (value is null)
        {
            return;
        }

        AppendPair(sb, key, value.Value ? "True" : "False");
    }

    private static string? FormatGenre(string? genre)
    {
        if (string.IsNullOrWhiteSpace(genre))
        {
            return null;
        }

        return GenreDisplayNames.TryGetValue(genre.Trim(), out string? display)
            ? display
            : genre;
    }

    private static string ToDifficulty(IReadOnlyDictionary<string, int> ranks, string primaryKey, IReadOnlyList<int> map, params string[] alternates)
    {
        if (!TryGetRank(ranks, primaryKey, alternates, out int rank))
        {
            return "-1";
        }

        return (RankToTier(map, rank) - 1).ToString();
    }

    private static bool TryGetRank(IReadOnlyDictionary<string, int> ranks, string primaryKey, IReadOnlyList<string> alternates, out int rank)
    {
        if (ranks.TryGetValue(primaryKey, out rank) && rank > 0)
        {
            return true;
        }

        foreach (string alternate in alternates)
        {
            if (ranks.TryGetValue(alternate, out rank) && rank > 0)
            {
                return true;
            }
        }

        rank = 0;
        return false;
    }

    private static bool HasRank(IReadOnlyDictionary<string, int> ranks, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (ranks.TryGetValue(key, out int rank) && rank > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static int RankToTier(IReadOnlyList<int> map, int rank)
    {
        int tier = 1;
        foreach (int threshold in map)
        {
            if (threshold <= rank)
            {
                tier++;
            }
        }

        return tier;
    }
}
