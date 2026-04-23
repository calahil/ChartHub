using ChartHub.Conversion.Audio;
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

/// <summary>
/// Integration tests that exercise the hash-table bounds fallback using a fan-made CON
/// whose group-2+ hash tables contain garbage next-block pointers (common output of
/// C3 CON Tools / Le Fluffie / RB3Maker).
/// </summary>
/// <remarks>
/// The file is skipped if the binary asset is absent (CI without merge assets).
/// </remarks>
public sealed class FanMadeConTests
{
    private static readonly string SuburbsConPath =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../merges/The Suburbs-1e613af8-e156-491a-b4a4-9a3a04ff3093.rb3con"));

    private static readonly string LocalDevSuburbsConPath =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../ChartHub.Server/dev-data/charthub/staging/jobs/405ca3d8-1099-4f97-874f-0a0059c4c3b1/The Suburbs-405ca3d8-1099-4f97-874f-0a0059c4c3b1.rb3con"));

    private static readonly string LocalDevNeighborhoodConPath =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../ChartHub.Server/dev-data/charthub/staging/jobs/7aeafca4-70c6-476d-b517-e9ffbce2be28/Neighborhood #1 (Tunnels)-7aeafca4-70c6-476d-b517-e9ffbce2be28.rb3con"));

    [Fact]
    public void GetAllFiles_FanMadeCon_ContainsExpectedEntries()
    {
        if (!File.Exists(SuburbsConPath))
        {
            return;
        }

        using var reader = StfsReader.Open(SuburbsConPath);
        var paths = reader.GetAllFiles().Select(f => f.VirtualPath).ToList();

        Assert.Contains(paths, p => p.EndsWith("songs.dta", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(paths, p => p.EndsWith(".mid", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(paths, p => p.EndsWith(".mogg", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReadFile_FanMadeCon_DtaContainsExpectedSongId()
    {
        if (!File.Exists(SuburbsConPath))
        {
            return;
        }

        using var reader = StfsReader.Open(SuburbsConPath);
        byte[]? dta = FindDtaAnyPath(reader);

        Assert.NotNull(dta);
        string text = System.Text.Encoding.Latin1.GetString(dta!);
        Assert.Contains("arcadefire", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadFile_FanMadeCon_MidiStartsWithMThd()
    {
        if (!File.Exists(SuburbsConPath))
        {
            return;
        }

        using var reader = StfsReader.Open(SuburbsConPath);
        (string VirtualPath, StfsEntry Entry) midEntry = reader.GetAllFiles()
            .FirstOrDefault(f => f.VirtualPath.EndsWith(".mid", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(midEntry.Entry);
        byte[] midi = reader.ReadEntry(midEntry.Entry);

        Assert.True(midi.Length >= 14, "MIDI data too short.");
        Assert.Equal(0x4D, midi[0]); // 'M'
        Assert.Equal(0x54, midi[1]); // 'T'
        Assert.Equal(0x68, midi[2]); // 'h'
        Assert.Equal(0x64, midi[3]); // 'd'
    }

    [Fact]
    public void ReadFile_FanMadeCon_MoggStartsWithMoggMagic()
    {
        if (!File.Exists(SuburbsConPath))
        {
            return;
        }

        using var reader = StfsReader.Open(SuburbsConPath);
        (string VirtualPath, StfsEntry Entry) moggEntry = reader.GetAllFiles()
            .FirstOrDefault(f => f.VirtualPath.EndsWith(".mogg", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(moggEntry.Entry);
        byte[] mogg = reader.ReadEntry(moggEntry.Entry);

        Assert.True(mogg.Length > 16, "MOGG data too short.");
        Assert.Equal(0x0A, mogg[0]);
    }

    [Fact]
    public async Task ConvertAsync_FanMadeSuburbsCon_ProducesSongFolder()
    {
        string? conPath = ResolveAvailableSuburbsConPath();
        if (conPath is null)
        {
            return;
        }

        string outputRoot = Path.Combine(Path.GetTempPath(), $"charthub-convert-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(outputRoot);

            var service = new ConversionService();
            Conversion.Models.ConversionResult result = await service.ConvertAsync(conPath, outputRoot);

            Assert.True(Directory.Exists(result.OutputDirectory));
            Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "song.ini")));
            Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "song.ogg")));
        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ConvertAsync_LocalNeighborhoodCon_SanitizesOutputPathAndSucceeds()
    {
        if (!File.Exists(LocalDevNeighborhoodConPath))
        {
            return;
        }

        string outputRoot = Path.Combine(Path.GetTempPath(), $"charthub-convert-neighborhood-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(outputRoot);

            var service = new ConversionService();
            Conversion.Models.ConversionResult result = await service.ConvertAsync(LocalDevNeighborhoodConPath, outputRoot);

            Assert.True(Directory.Exists(result.OutputDirectory));
            Assert.DoesNotContain("#", result.OutputDirectory, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "song.ini")));
            Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "song.ogg")));
        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    private static string? ResolveAvailableSuburbsConPath()
    {
        if (File.Exists(SuburbsConPath))
        {
            return SuburbsConPath;
        }

        if (File.Exists(LocalDevSuburbsConPath))
        {
            return LocalDevSuburbsConPath;
        }

        return null;
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

public sealed class StfsReaderSafetyTests
{
    [Fact]
    public void ReadEntry_MalformedNextBlockPointer_FallsBackToSequentialBlocks()
    {
        using MemoryStream stream = BuildSyntheticConWithMalformedNextPointer();
        using var reader = StfsReader.Open(stream);

        (string _, StfsEntry entry) = reader.GetAllFiles().Single();
        byte[] data = reader.ReadEntry(entry);

        Assert.Equal(5000, data.Length);
        Assert.Equal((byte)0x11, data[0]);
        Assert.Equal((byte)0x11, data[4095]);
        Assert.Equal((byte)0x22, data[4096]);
        Assert.Equal((byte)0x22, data[^1]);
    }

    private static MemoryStream BuildSyntheticConWithMalformedNextPointer()
    {
        const int blockSize = 0x1000;
        const int headerSize = 0x1000;
        const int firstDataBlock = 170;
        const int secondDataBlock = 171;

        long dirBlockOffset = headerSize + (0 + 0 + 1) * blockSize;
        long firstDataOffset = headerSize + (firstDataBlock + (firstDataBlock / 170) + 1) * blockSize;
        long secondDataOffset = headerSize + (secondDataBlock + (secondDataBlock / 170) + 1) * blockSize;
        long groupOneHashOffset = headerSize + (1L * 171 * blockSize);
        int streamLength = (int)(secondDataOffset + blockSize);

        byte[] bytes = new byte[streamLength];

        // Header magic.
        bytes[0] = 0x43; // C
        bytes[1] = 0x4F; // O
        bytes[2] = 0x4E; // N
        bytes[3] = 0x20; // space

        // Raw header size at 0x340 (BE32), rounded to 0x1000 by reader.
        bytes[0x343] = 0x40;

        // File table metadata at 0x37C-0x380.
        bytes[0x37C] = 0x01; // file table block count = 1 (LE16)
        bytes[0x37D] = 0x00;
        bytes[0x37E] = 0x00; // file table first block = 0 (LE24)
        bytes[0x37F] = 0x00;
        bytes[0x380] = 0x00;

        int dir = (int)dirBlockOffset;
        string name = "song.bin";
        for (int i = 0; i < name.Length; i++)
        {
            bytes[dir + i] = (byte)name[i];
        }

        bytes[dir + 0x28] = (byte)name.Length; // flags: file + name length
        bytes[dir + 0x2F] = (byte)(firstDataBlock & 0xFF);
        bytes[dir + 0x30] = (byte)((firstDataBlock >> 8) & 0xFF);
        bytes[dir + 0x31] = (byte)((firstDataBlock >> 16) & 0xFF);
        bytes[dir + 0x32] = 0xFF;
        bytes[dir + 0x33] = 0xFF; // root parent marker

        const int fileSize = 5000;
        bytes[dir + 0x34] = (byte)((fileSize >> 24) & 0xFF);
        bytes[dir + 0x35] = (byte)((fileSize >> 16) & 0xFF);
        bytes[dir + 0x36] = (byte)((fileSize >> 8) & 0xFF);
        bytes[dir + 0x37] = (byte)(fileSize & 0xFF);

        // Group 1, index 0 hash entry describes block 170. Set a malformed explicit pointer.
        int hash = (int)groupOneHashOffset;
        bytes[hash + 20] = 0x01; // explicit pointer
        bytes[hash + 21] = 0xFF;
        bytes[hash + 22] = 0xFF;
        bytes[hash + 23] = 0xFE; // absurd next block -> should trigger bounds fallback to n+1

        Array.Fill(bytes, (byte)0x11, (int)firstDataOffset, blockSize);
        Array.Fill(bytes, (byte)0x22, (int)secondDataOffset, blockSize);

        return new MemoryStream(bytes, writable: false);
    }
}

public sealed class MoggExtractorTests
{
    [Fact]
    public async Task ExtractStemsAsync_InvalidStemChannels_AreSkipped()
    {
        if (OperatingSystem.IsWindows())
        {
            // Test harness uses a tiny bash ffmpeg stub.
            return;
        }

        string tempRoot = Path.Combine(Path.GetTempPath(), $"charthub-mogg-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            string outputDir = Path.Combine(tempRoot, "out");
            Directory.CreateDirectory(outputDir);

            string invocationLog = Path.Combine(tempRoot, "ffmpeg-invocations.log");
            string fakeFfmpeg = CreateFakeFfmpegScript(tempRoot, invocationLog);

            var extractor = new MoggExtractor(fakeFfmpeg);
            DtaSongInfo songInfo = new()
            {
                ShortName = "suburbs",
                Title = "The Suburbs",
                Artist = "Arcade Fire",
                TrackChannels = new Dictionary<string, IReadOnlyList<int>>
                {
                    ["guitar"] = [0, 1],
                    ["keys"] = [8, 9], // Invalid for a 2-channel Vorbis stream.
                },
                TotalChannels = 10,
            };

            byte[] moggBytes = BuildSyntheticMoggWithVorbisChannels(2);
            IReadOnlyDictionary<string, string> stems = await extractor.ExtractStemsAsync(moggBytes, songInfo, outputDir);

            Assert.Contains("song", stems.Keys);
            Assert.Contains("guitar", stems.Keys);
            Assert.DoesNotContain("keys", stems.Keys);

            string[] invocations = File.ReadAllLines(invocationLog);
            Assert.Equal(2, invocations.Length); // backing + guitar stem only
            Assert.All(invocations, invocation => Assert.DoesNotContain("-filter_complex", invocation, StringComparison.Ordinal));
            Assert.Contains("-af", invocations[1], StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExtractStemsAsync_BogusHeaderOffset_RecoversByOggSyncScan()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string tempRoot = Path.Combine(Path.GetTempPath(), $"charthub-mogg-offset-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            string outputDir = Path.Combine(tempRoot, "out");
            Directory.CreateDirectory(outputDir);

            string invocationLog = Path.Combine(tempRoot, "ffmpeg-invocations.log");
            string fakeFfmpeg = CreateFakeFfmpegScript(tempRoot, invocationLog);

            var extractor = new MoggExtractor(fakeFfmpeg);
            DtaSongInfo songInfo = new()
            {
                ShortName = "suburbs",
                Title = "The Suburbs",
                Artist = "Arcade Fire",
                TrackChannels = new Dictionary<string, IReadOnlyList<int>>
                {
                    ["guitar"] = [0, 1],
                },
                TotalChannels = 2,
            };

            // Header claims 0x20, but actual Ogg payload starts at 12.
            // This forces recovery through the OggS sync scan path.
            byte[] moggBytes = BuildSyntheticMoggWithVorbisChannels(2, declaredOffset: 0x20, actualOffset: 12);
            IReadOnlyDictionary<string, string> stems = await extractor.ExtractStemsAsync(moggBytes, songInfo, outputDir);

            Assert.Contains("song", stems.Keys);
            Assert.Contains("guitar", stems.Keys);
            Assert.True(File.Exists(stems["song"]));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static string CreateFakeFfmpegScript(string root, string invocationLog)
    {
        string scriptPath = Path.Combine(root, "fake-ffmpeg.sh");
        string script = "#!/usr/bin/env bash\n"
            + "set -e\n"
            + "in=\"\"\n"
            + "for ((i=1;i<=$#;i++)); do\n"
            + "  if [[ \"${!i}\" == \"-i\" ]]; then\n"
            + "    j=$((i+1))\n"
            + "    in=\"${!j}\"\n"
            + "    break\n"
            + "  fi\n"
            + "done\n"
            + "if [[ -n \"$in\" ]]; then\n"
            + "  sig=$(head -c 4 \"$in\" || true)\n"
            + "  if [[ \"$sig\" != \"OggS\" ]]; then\n"
            + "    echo \"invalid-ogg\" >&2\n"
            + "    exit 9\n"
            + "  fi\n"
            + "fi\n"
            + $"echo \"$*\" >> \"{invocationLog}\"\n"
            + "out=\"${@: -1}\"\n"
            + "mkdir -p \"$(dirname \"$out\")\"\n"
            + ": > \"$out\"\n";
        File.WriteAllText(scriptPath, script);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(scriptPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        return scriptPath;
    }

    private static byte[] BuildSyntheticMoggWithVorbisChannels(byte channels, int declaredOffset = 8, int actualOffset = 8)
    {
        byte[] rawOgg = new byte[64];
        rawOgg[0] = (byte)'O';
        rawOgg[1] = (byte)'g';
        rawOgg[2] = (byte)'g';
        rawOgg[3] = (byte)'S';
        int marker = 16;
        rawOgg[marker] = 0x01;
        rawOgg[marker + 1] = (byte)'v';
        rawOgg[marker + 2] = (byte)'o';
        rawOgg[marker + 3] = (byte)'r';
        rawOgg[marker + 4] = (byte)'b';
        rawOgg[marker + 5] = (byte)'i';
        rawOgg[marker + 6] = (byte)'s';
        rawOgg[marker + 11] = channels; // packet_type + "vorbis" + version(4) + channels

        if (actualOffset < 8)
        {
            throw new ArgumentOutOfRangeException(nameof(actualOffset), "MOGG payload offset must be at least 8.");
        }

        byte[] mogg = new byte[actualOffset + rawOgg.Length];
        mogg[0] = 0x0A;
        mogg[4] = (byte)(declaredOffset & 0xFF);
        mogg[5] = (byte)((declaredOffset >> 8) & 0xFF);
        mogg[6] = (byte)((declaredOffset >> 16) & 0xFF);
        mogg[7] = (byte)((declaredOffset >> 24) & 0xFF);
        Buffer.BlockCopy(rawOgg, 0, mogg, actualOffset, rawOgg.Length);
        return mogg;
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
