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

    /// <summary>Generates <c>song.ini</c> content from DTA song info.</summary>
    public static string Generate(DtaSongInfo info)
    {
        var sb = new StringBuilder();
        sb.Append("[song]\r\n");
        AppendPair(sb, "name", info.Title);
        AppendPair(sb, "artist", info.Artist);
        AppendPair(sb, "album", info.Album);
        AppendPair(sb, "charter", info.Charter);
        AppendPair(sb, "frets", info.Charter);
        AppendPair(sb, "year", info.Year);
        AppendPair(sb, "genre", info.Genre);
        AppendPair(sb, "song_length", info.SongLengthMs > 0 ? info.SongLengthMs.ToString() : null);
        AppendPair(sb, "preview_start_time", info.PreviewStartMs > 0 ? info.PreviewStartMs.ToString() : null);
        AppendPair(sb, "preview_end_time", info.PreviewEndMs > 0 ? info.PreviewEndMs.ToString() : null);
        AppendPair(sb, "diff_band", ToDifficulty(info.Ranks, "band", BandDiffMap));
        AppendPair(sb, "diff_guitar", ToDifficulty(info.Ranks, "guitar", GuitarDiffMap));
        AppendPair(sb, "diff_bass", ToDifficulty(info.Ranks, "bass", BassDiffMap));
        AppendPair(sb, "diff_drums", ToDifficulty(info.Ranks, "drum", DrumsDiffMap, "drums"));
        AppendPair(sb, "diff_keys", ToDifficulty(info.Ranks, "keys", KeysDiffMap));
        AppendPair(sb, "diff_vocals", ToDifficulty(info.Ranks, "vocals", VocalDiffMap, "vocal"));
        AppendPair(sb, "diff_vocals_harm", info.VocalParts > 1 ? ToDifficulty(info.Ranks, "vocals", VocalDiffMap, "vocal") : "-1");
        AppendPair(sb, "track", info.AlbumTrack > 0 ? info.AlbumTrack.ToString() : null);
        AppendPair(sb, "album_track", info.AlbumTrack > 0 ? info.AlbumTrack.ToString() : null);
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
        if (ranks.TryGetValue(primaryKey, out rank))
        {
            return true;
        }

        foreach (string alternate in alternates)
        {
            if (ranks.TryGetValue(alternate, out rank))
            {
                return true;
            }
        }

        rank = 0;
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
