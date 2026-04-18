using System.Text;

using ChartHub.Conversion.Dta;

namespace ChartHub.Conversion.SongIni;

/// <summary>
/// Generates a Clone Hero–compatible <c>song.ini</c> file from Rock Band DTA metadata.
/// </summary>
internal static class SongIniGenerator
{
    /// <summary>Generates <c>song.ini</c> content from DTA song info.</summary>
    public static string Generate(DtaSongInfo info)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[song]");
        sb.AppendLine($"name = {Escape(info.Title)}");
        sb.AppendLine($"artist = {Escape(info.Artist)}");
        sb.AppendLine($"charter = {Escape(info.Charter)}");

        // Instrument availability derived from DTA track channel map
        sb.AppendLine($"diff_drums = {ToDiffFlag(info.TrackChannels.ContainsKey("drum") || info.TrackChannels.ContainsKey("drums"))}");
        sb.AppendLine($"diff_guitar = {ToDiffFlag(info.TrackChannels.ContainsKey("guitar"))}");
        sb.AppendLine($"diff_bass = {ToDiffFlag(info.TrackChannels.ContainsKey("bass"))}");
        sb.AppendLine($"diff_vocals = {ToDiffFlag(info.TrackChannels.ContainsKey("vocals") || info.TrackChannels.ContainsKey("vocal"))}");
        sb.AppendLine($"diff_keys = {ToDiffFlag(info.TrackChannels.ContainsKey("keys"))}");

        // Use 0 (auto) for difficulties; downstream tools or users can override.
        sb.AppendLine("diff_band = -1");

        return sb.ToString();
    }

    private static string Escape(string value)
    {
        // song.ini does not support multi-line values; replace control characters.
        return value.Replace('\n', ' ').Replace('\r', ' ');
    }

    private static int ToDiffFlag(bool present) => present ? 0 : -1;
}
