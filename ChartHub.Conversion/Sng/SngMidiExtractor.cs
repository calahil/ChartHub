using ChartHub.Conversion.Midi;

namespace ChartHub.Conversion.Sng;

/// <summary>
/// Extracts and converts MIDI content from a parsed SNGPKG container.
/// </summary>
internal static class SngMidiExtractor
{
    internal static IReadOnlyList<SngChartContent> ExtractCloneHeroCharts(SngPackage package, byte[] containerBytes)
    {
        List<SngChartContent> charts = [];

        if (SngPackageReader.TryFindEntry(package, "notes.mid", out SngFileEntry? midiEntry)
            && midiEntry != null)
        {
            byte[] midiBytes = SngPackageReader.ReadFileData(containerBytes, midiEntry);

            // Some fan-made SNG packages ship a non-RB-standard notes.mid payload.
            // In that case preserve the payload rather than failing conversion.
            byte[] canonicalMidi = StartsWithMidiHeader(midiBytes)
                ? RbMidiConverter.Convert(midiBytes)
                : midiBytes;
            charts.Add(new SngChartContent("notes.mid", canonicalMidi));
        }

        if (SngPackageReader.TryFindEntry(package, "notes.chart", out SngFileEntry? chartEntry)
            && chartEntry != null)
        {
            charts.Add(new SngChartContent("notes.chart", SngPackageReader.ReadFileData(containerBytes, chartEntry)));
        }

        if (charts.Count == 0)
        {
            throw new InvalidDataException("No notes.mid or notes.chart entry found in SNG package.");
        }

        return charts;
    }

    private static bool StartsWithMidiHeader(ReadOnlySpan<byte> bytes)
    {
        return bytes.Length >= 4
            && bytes[0] == (byte)'M'
            && bytes[1] == (byte)'T'
            && bytes[2] == (byte)'h'
            && bytes[3] == (byte)'d';
    }
}

internal readonly record struct SngChartContent(string FileName, byte[] Bytes);
