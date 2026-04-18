using ChartHub.Conversion.Dta;
using ChartHub.Conversion.Stfs;

namespace ChartHub.Conversion.Tests;

/// <summary>
/// Integration tests for <see cref="StfsReader"/> using the real DontStay_rb3con sample file.
/// </summary>
/// <remarks>
/// These tests require the sample CON files to be available at the relative path
/// <c>../../../../merges/</c> from the test output directory. They are skipped if the
/// files are absent (CI environments without the binary assets).
/// </remarks>
public sealed class StfsReaderTests
{
    private static readonly string SampleConPath =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../merges/DontStay_rb3con"));

    [Fact]
    public void Open_ValidConFile_DoesNotThrow()
    {
        if (!File.Exists(SampleConPath))
        {
            return;
        }

        using var reader = StfsReader.Open(SampleConPath);
        Assert.NotNull(reader);
    }

    [Fact]
    public void GetAllFiles_DontStayCon_ContainsExpectedFiles()
    {
        if (!File.Exists(SampleConPath))
        {
            return;
        }

        using var reader = StfsReader.Open(SampleConPath);

        var files = reader.GetAllFiles().Select(f => f.VirtualPath).ToList();

        Assert.Contains(files, p => p.EndsWith("songs.dta", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(files, p => p.EndsWith(".mid", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(files, p => p.EndsWith(".mogg", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReadFile_SongsDta_ParseableContent()
    {
        if (!File.Exists(SampleConPath))
        {
            return;
        }

        using var reader = StfsReader.Open(SampleConPath);

        byte[]? dta = reader.ReadFile("songs/dontstay/songs.dta");
        dta ??= FindDtaAnyPath(reader);

        Assert.NotNull(dta);
        Assert.True(dta!.Length > 100, $"songs.dta too small: {dta.Length} bytes");

        // Must contain recognisable DTA content
        string text = System.Text.Encoding.Latin1.GetString(dta);
        Assert.Contains("DontStay", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadFile_MidiFile_StartsWithMThd()
    {
        if (!File.Exists(SampleConPath))
        {
            return;
        }

        using var reader = StfsReader.Open(SampleConPath);

        (string VirtualPath, StfsEntry Entry) midEntry = reader.GetAllFiles()
            .FirstOrDefault(f => f.VirtualPath.EndsWith(".mid", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(midEntry.Entry);
        byte[] midi = reader.ReadEntry(midEntry.Entry);

        Assert.True(midi.Length >= 14, "MIDI data too short to contain MThd.");
        Assert.Equal(0x4D, midi[0]); // 'M'
        Assert.Equal(0x54, midi[1]); // 'T'
        Assert.Equal(0x68, midi[2]); // 'h'
        Assert.Equal(0x64, midi[3]); // 'd'
    }

    [Fact]
    public void ReadFile_MoggFile_StartsWithMoggMagic()
    {
        if (!File.Exists(SampleConPath))
        {
            return;
        }

        using var reader = StfsReader.Open(SampleConPath);

        (string VirtualPath, StfsEntry Entry) moggEntry = reader.GetAllFiles()
            .FirstOrDefault(f => f.VirtualPath.EndsWith(".mogg", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(moggEntry.Entry);
        byte[] mogg = reader.ReadEntry(moggEntry.Entry);

        Assert.True(mogg.Length > 16, "MOGG data too short.");
        // MOGG magic: first byte is version (0x0A for RB2/RB3 unencrypted)
        Assert.Equal(0x0A, mogg[0]);
    }

    private static byte[]? FindDtaAnyPath(StfsReader reader)
    {
        foreach ((string path, _) in reader.GetAllFiles())
        {
            if (path.EndsWith("songs.dta", StringComparison.OrdinalIgnoreCase))
            {
                return reader.ReadFile(path);
            }
        }

        return null;
    }
}

/// <summary>Unit tests for <see cref="DtaParser"/>.</summary>
public sealed class DtaParserTests
{
    private const string SampleDta = """
        ('DontStay'
          ('name' "Don't Stay")
          ('artist' "Linkin Park")
          ('charter' "TestCharter")
          ('song'
            ('name' "songs/dontstay/dontstay")
            ('tracks_count' (2 2 2 2 2 0))
            ('tracks'
              (('drum' (0 1))
               ('bass' (2 3))
               ('guitar' (4 5))
               ('vocals' (6 7))
               ('keys' (8 9))))
            ('pans' (-1.0 1.0 -1.0 1.0 -1.0 1.0 -1.0 1.0 -1.0 1.0))
            ('vols' (0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0))
          )
        )
        """;

    [Fact]
    public void Parse_ValidDta_ExtractsTitle()
    {
        DtaSongInfo info = DtaParser.Parse(System.Text.Encoding.Latin1.GetBytes(SampleDta));
        Assert.Equal("Don't Stay", info.Title);
    }

    [Fact]
    public void Parse_ValidDta_ExtractsArtist()
    {
        DtaSongInfo info = DtaParser.Parse(System.Text.Encoding.Latin1.GetBytes(SampleDta));
        Assert.Equal("Linkin Park", info.Artist);
    }

    [Fact]
    public void Parse_ValidDta_ExtractsCharter()
    {
        DtaSongInfo info = DtaParser.Parse(System.Text.Encoding.Latin1.GetBytes(SampleDta));
        Assert.Equal("TestCharter", info.Charter);
    }

    [Fact]
    public void Parse_ValidDta_ExtractsDrumChannels()
    {
        DtaSongInfo info = DtaParser.Parse(System.Text.Encoding.Latin1.GetBytes(SampleDta));
        Assert.True(info.TrackChannels.ContainsKey("drum"), "drum channels not found");
        Assert.Equal([0, 1], info.TrackChannels["drum"]);
    }

    [Fact]
    public void Parse_ValidDta_ExtractsTotalChannels()
    {
        DtaSongInfo info = DtaParser.Parse(System.Text.Encoding.Latin1.GetBytes(SampleDta));
        Assert.Equal(10, info.TotalChannels);
    }
}

/// <summary>Unit tests for <see cref="RbMidiConverter"/>.</summary>
public sealed class RbMidiConverterTests
{
    private static byte[] BuildMinimalMidi(params (string trackName, bool isEmpty)[] tracks)
    {
        using var ms = new MemoryStream();

        // MThd
        ms.Write("MThd"u8);
        WriteUInt32BE(ms, 6);
        WriteUInt16BE(ms, 1); // format 1
        WriteUInt16BE(ms, (ushort)(1 + tracks.Length)); // tempo track + named tracks
        WriteUInt16BE(ms, 480); // division

        // Tempo track
        WriteMidiTrack(ms, "TEMPO TRACK");

        foreach ((string name, _) in tracks)
        {
            WriteMidiTrack(ms, name);
        }

        return ms.ToArray();
    }

    private static void WriteMidiTrack(MemoryStream ms, string name)
    {
        using var track = new MemoryStream();
        // Delta 0, meta FF 03 Name
        track.WriteByte(0x00); // delta
        track.WriteByte(0xFF); // meta
        track.WriteByte(0x03); // track name
        track.WriteByte((byte)name.Length);
        track.Write(System.Text.Encoding.ASCII.GetBytes(name));
        // End of track
        track.WriteByte(0x00);
        track.WriteByte(0xFF);
        track.WriteByte(0x2F);
        track.WriteByte(0x00);

        byte[] data = track.ToArray();
        ms.Write("MTrk"u8);
        WriteUInt32BE(ms, (uint)data.Length);
        ms.Write(data);
    }

    private static void WriteUInt16BE(MemoryStream s, ushort v) { s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }
    private static void WriteUInt32BE(MemoryStream s, uint v) { s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }

    [Fact]
    public void Convert_StripsBeatTrack()
    {
        byte[] midi = BuildMinimalMidi(("PART GUITAR", false), ("BEAT", false));
        byte[] output = Midi.RbMidiConverter.Convert(midi);

        // Parse output track names
        List<string> names = ExtractTrackNames(output);
        Assert.DoesNotContain("BEAT", names);
        Assert.Contains("PART GUITAR", names);
    }

    [Fact]
    public void Convert_StripsVenueTrack()
    {
        byte[] midi = BuildMinimalMidi(("PART DRUMS", false), ("VENUE", false));
        byte[] output = Midi.RbMidiConverter.Convert(midi);

        List<string> names = ExtractTrackNames(output);
        Assert.DoesNotContain("VENUE", names);
        Assert.Contains("PART DRUMS", names);
    }

    [Fact]
    public void Convert_KeepsPartGuitar()
    {
        byte[] midi = BuildMinimalMidi(("PART GUITAR", false));
        byte[] output = Midi.RbMidiConverter.Convert(midi);

        List<string> names = ExtractTrackNames(output);
        Assert.Contains("PART GUITAR", names);
    }

    private static List<string> ExtractTrackNames(byte[] midi)
    {
        var names = new List<string>();
        int pos = 14; // skip MThd
        int trackCount = (midi[10] << 8) | midi[11];
        for (int t = 0; t < trackCount; t++)
        {
            if (pos + 8 > midi.Length)
            {
                break;
            }

            int len = (midi[pos + 4] << 24) | (midi[pos + 5] << 16) | (midi[pos + 6] << 8) | midi[pos + 7];
            byte[] data = midi[(pos + 8)..(pos + 8 + len)];
            pos += 8 + len;
            // Find FF 03 <len> <name>
            for (int i = 0; i < data.Length - 3; i++)
            {
                if (data[i] == 0xFF && data[i + 1] == 0x03)
                {
                    int nlen = data[i + 2];
                    if (i + 3 + nlen <= data.Length)
                    {
                        names.Add(System.Text.Encoding.ASCII.GetString(data, i + 3, nlen));
                    }

                    break;
                }
            }
        }

        return names;
    }
}
