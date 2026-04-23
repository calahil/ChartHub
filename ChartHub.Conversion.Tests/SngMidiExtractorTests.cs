using System.Text;

using ChartHub.Conversion.Sng;

namespace ChartHub.Conversion.Tests;

public sealed class SngMidiExtractorTests
{
    private static readonly string MidiFixturePath =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../merges/Pearl Jam - Yellow Ledbetter (farottone).sng"));

    [Fact]
    public void ExtractCloneHeroMidi_WhenNotesMidPresent_ReturnsConvertedMidi()
    {
        byte[] containerBytes = BuildSyntheticSngPkg(
            [
                ("notes.mid", BuildRbMidiWithVenueTrack()),
                ("song.opus", [0x01, 0x02, 0x03]),
            ]);

        SngPackage package = SngPackageReader.Read(containerBytes);
        byte[] converted = SngMidiExtractor.ExtractCloneHeroMidi(package, containerBytes);

        Assert.True(converted.Length >= 14);
        Assert.Equal((byte)'M', converted[0]);
        Assert.Equal((byte)'T', converted[1]);
        Assert.Equal((byte)'h', converted[2]);
        Assert.Equal((byte)'d', converted[3]);
        Assert.Equal(2, ReadUInt16BE(converted, 10));
    }

    [Fact]
    public void ExtractCloneHeroMidi_WhenOnlyNotesChartPresent_ThrowsNotSupported()
    {
        byte[] containerBytes = BuildSyntheticSngPkg(
            [
                ("notes.chart", Encoding.ASCII.GetBytes("chart")),
                ("song.opus", [0x01, 0x02, 0x03]),
            ]);

        SngPackage package = SngPackageReader.Read(containerBytes);

        NotSupportedException ex = Assert.Throws<NotSupportedException>(
            () => SngMidiExtractor.ExtractCloneHeroMidi(package, containerBytes));

        Assert.Contains("notes.chart", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractCloneHeroMidi_WhenNotesMidIsNonStandard_PassesBytesThrough()
    {
        byte[] nonStandardBytes = Encoding.ASCII.GetBytes("NOT-A-STANDARD-MIDI");
        byte[] containerBytes = BuildSyntheticSngPkg(
            [
                ("notes.mid", nonStandardBytes),
                ("song.opus", [0x01, 0x02, 0x03]),
            ]);

        SngPackage package = SngPackageReader.Read(containerBytes);
        byte[] extracted = SngMidiExtractor.ExtractCloneHeroMidi(package, containerBytes);

        Assert.Equal(nonStandardBytes, extracted);
    }

    [Fact]
    public void ExtractCloneHeroMidi_RealFixtureWithNotesMid_ParsesAndConverts()
    {
        if (!File.Exists(MidiFixturePath))
        {
            return;
        }

        byte[] containerBytes = File.ReadAllBytes(MidiFixturePath);
        SngPackage package = SngPackageReader.Read(containerBytes);
        byte[] converted = SngMidiExtractor.ExtractCloneHeroMidi(package, containerBytes);

        Assert.True(converted.Length >= 14);
        Assert.Equal((byte)'M', converted[0]);
        Assert.Equal((byte)'T', converted[1]);
        Assert.Equal((byte)'h', converted[2]);
        Assert.Equal((byte)'d', converted[3]);
    }

    private static byte[] BuildSyntheticSngPkg(IReadOnlyList<(string Name, byte[] Data)> files)
    {
        int tableSize = files.Sum(f => 1 + Encoding.ASCII.GetByteCount(f.Name) + 16);
        int dataStart = 64 + tableSize;
        int dataLength = files.Sum(f => f.Data.Length);

        byte[] bytes = new byte[dataStart + dataLength + 16];

        Array.Copy(Encoding.ASCII.GetBytes("SNGPKG"), 0, bytes, 0, 6);
        bytes[6] = 1;

        int tablePos = 64;
        int currentOffset = dataStart;

        foreach ((string name, byte[] data) in files)
        {
            tablePos = WriteEntry(bytes, tablePos, name, (ulong)currentOffset, (ulong)data.Length);
            Buffer.BlockCopy(data, 0, bytes, currentOffset, data.Length);
            currentOffset += data.Length;
        }

        return bytes;
    }

    private static byte[] BuildRbMidiWithVenueTrack()
    {
        byte[] tempoTrack = BuildTrack([]);
        byte[] venueTrack = BuildTrackNameTrack("VENUE");
        byte[] guitarTrack = BuildTrackNameTrack("PART GUITAR");

        using var stream = new MemoryStream();
        stream.Write("MThd"u8);
        WriteUInt32BE(stream, 6);
        WriteUInt16BE(stream, 1);
        WriteUInt16BE(stream, 3);
        WriteUInt16BE(stream, 480);

        WriteTrack(stream, tempoTrack);
        WriteTrack(stream, venueTrack);
        WriteTrack(stream, guitarTrack);

        return stream.ToArray();
    }

    private static byte[] BuildTrackNameTrack(string trackName)
    {
        byte[] nameBytes = Encoding.ASCII.GetBytes(trackName);
        return BuildTrack(
            [
                0x00,
                0xFF,
                0x03,
                (byte)nameBytes.Length,
                .. nameBytes,
                0x00,
                0xFF,
                0x2F,
                0x00,
            ]);
    }

    private static byte[] BuildTrack(byte[] events)
    {
        using var stream = new MemoryStream();
        stream.Write("MTrk"u8);
        WriteUInt32BE(stream, (uint)events.Length);
        stream.Write(events);
        return stream.ToArray();
    }

    private static void WriteTrack(Stream output, byte[] trackChunk)
    {
        output.Write(trackChunk);
    }

    private static int WriteEntry(byte[] bytes, int pos, string name, ulong offset, ulong length)
    {
        byte[] nameBytes = Encoding.ASCII.GetBytes(name);
        bytes[pos++] = (byte)nameBytes.Length;
        Array.Copy(nameBytes, 0, bytes, pos, nameBytes.Length);
        pos += nameBytes.Length;

        WriteUInt64LE(bytes, pos, offset);
        pos += 8;
        WriteUInt64LE(bytes, pos, length);
        pos += 8;

        return pos;
    }

    private static int ReadUInt16BE(byte[] bytes, int offset)
    {
        return (bytes[offset] << 8) | bytes[offset + 1];
    }

    private static void WriteUInt16BE(Stream stream, ushort value)
    {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)(value & 0xFF));
    }

    private static void WriteUInt32BE(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)((value >> 16) & 0xFF));
        stream.WriteByte((byte)((value >> 8) & 0xFF));
        stream.WriteByte((byte)(value & 0xFF));
    }

    private static void WriteUInt64LE(byte[] bytes, int pos, ulong value)
    {
        bytes[pos] = (byte)(value & 0xFF);
        bytes[pos + 1] = (byte)((value >> 8) & 0xFF);
        bytes[pos + 2] = (byte)((value >> 16) & 0xFF);
        bytes[pos + 3] = (byte)((value >> 24) & 0xFF);
        bytes[pos + 4] = (byte)((value >> 32) & 0xFF);
        bytes[pos + 5] = (byte)((value >> 40) & 0xFF);
        bytes[pos + 6] = (byte)((value >> 48) & 0xFF);
        bytes[pos + 7] = (byte)((value >> 56) & 0xFF);
    }
}