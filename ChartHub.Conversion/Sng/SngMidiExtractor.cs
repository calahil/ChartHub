using ChartHub.Conversion.Midi;

namespace ChartHub.Conversion.Sng;

/// <summary>
/// Extracts and converts MIDI content from a parsed SNGPKG container.
/// </summary>
internal static class SngMidiExtractor
{
    internal static byte[] ExtractCloneHeroMidi(SngPackage package, byte[] containerBytes)
    {
        if (SngPackageReader.TryFindEntry(package, "notes.mid", out SngFileEntry? midiEntry)
            && midiEntry != null)
        {
            byte[] midiBytes = SngPackageReader.ReadFileData(containerBytes, midiEntry);

            // Some fan-made SNG packages ship a non-RB-standard notes.mid payload.
            // In that case preserve the payload rather than failing conversion.
            if (!StartsWithMidiHeader(midiBytes))
            {
                return midiBytes;
            }

            return RbMidiConverter.Convert(midiBytes);
        }

        if (SngPackageReader.TryFindEntry(package, "notes.chart", out SngFileEntry? chartEntry)
            && chartEntry != null)
        {
            throw new NotSupportedException(
                "SNG packages with notes.chart are not supported yet. Expected notes.mid for RB MIDI conversion reuse.");
        }

        throw new InvalidDataException("No notes.mid or notes.chart entry found in SNG package.");
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
