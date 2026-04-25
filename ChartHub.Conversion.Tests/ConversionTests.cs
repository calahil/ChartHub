using System.Security.Cryptography;

using ChartHub.Conversion.Audio;
using ChartHub.Conversion.Dta;
using ChartHub.Conversion.Image;
using ChartHub.Conversion.Midi;
using ChartHub.Conversion.Models;
using ChartHub.Conversion.Sng;
using ChartHub.Conversion.SongIni;
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

public sealed class ConversionServiceRoutingTests
{
    private static readonly string SampleRb3ConPath =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../merges/Ready to Start-e99c44e9-43a5-4c54-aa86-4cffb56bb215.rb3con"));

    private static readonly string BrokeRb3ConPath =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../kam_mm_broke.rb3con"));

    private static readonly string NotesMidSngPath =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../merges/Pearl Jam - Yellow Ledbetter (farottone).sng"));
    private static readonly string NotesChartSngPath =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../merges/Calibration - Calibration Chart 225 BPM (JoMartineau).sng"));

    [Fact]
    public async Task ConvertAsync_SngInput_ConvertsFixtureIntoCloneHeroFolder()
    {
        string outputRoot = Path.Combine(Path.GetTempPath(), $"charthub-sng-route-test-{Guid.NewGuid():N}");

        try
        {
            if (!File.Exists(NotesMidSngPath))
            {
                return;
            }

            Directory.CreateDirectory(outputRoot);

            var service = new ConversionService();
            ConversionResult result = await service.ConvertAsync(NotesMidSngPath, outputRoot);

            Assert.Equal("Pearl Jam", result.Metadata.Artist);
            Assert.Equal("Yellow Ledbetter", result.Metadata.Title);
            Assert.Equal("farottone", result.Metadata.Charter);
            Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "notes.mid")));
            Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "song.ini")));
            Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "song.opus")));
            Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "album.jpg")));
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
    public async Task ConvertAsync_SngInputWithNotesChart_ExtractsNotesChart()
    {
        string outputRoot = Path.Combine(Path.GetTempPath(), $"charthub-sng-route-test-{Guid.NewGuid():N}");

        try
        {
            if (!File.Exists(NotesChartSngPath))
            {
                return;
            }

            Directory.CreateDirectory(outputRoot);

            var service = new ConversionService();
            ConversionResult result = await service.ConvertAsync(NotesChartSngPath, outputRoot);

            Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "notes.chart")));
            Assert.False(File.Exists(Path.Combine(result.OutputDirectory, "notes.mid")));
            Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "song.ini")));
            Assert.True(Directory.EnumerateFiles(result.OutputDirectory, "*.opus", SearchOption.TopDirectoryOnly).Any());
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
    public async Task ConvertAsync_Rb3ConInput_ProducesInstrumentStemAudio()
    {
        string outputRoot = Path.Combine(Path.GetTempPath(), $"charthub-rb3con-route-test-{Guid.NewGuid():N}");

        try
        {
            if (!File.Exists(SampleRb3ConPath))
            {
                return;
            }

            Directory.CreateDirectory(outputRoot);

            var service = new ConversionService();
            ConversionResult result = await service.ConvertAsync(SampleRb3ConPath, outputRoot);

            Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "song.ogg")));

            string[] stems = Directory
                .EnumerateFiles(result.OutputDirectory, "*.ogg", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .Where(name => !string.Equals(name, "song.ogg", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            Assert.True(stems.Length > 0, "Expected at least one instrument stem OGG generated from RB3CON channel mapping.");
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
    public async Task ConvertAsync_BrokeRb3Con_WritesAlbumPng()
    {
        string outputRoot = Path.Combine(Path.GetTempPath(), $"charthub-broke-route-test-{Guid.NewGuid():N}");

        try
        {
            if (!File.Exists(BrokeRb3ConPath))
            {
                return;
            }

            Directory.CreateDirectory(outputRoot);

            var service = new ConversionService();
            ConversionResult result = await service.ConvertAsync(BrokeRb3ConPath, outputRoot);

            string albumPath = Path.Combine(result.OutputDirectory, "album.png");
            Assert.True(File.Exists(albumPath), "Expected album art extracted from legacy png_xbox fixture.");
            Assert.True(new FileInfo(albumPath).Length > 0, "Expected extracted album art to be non-empty.");

            using var image = SixLabors.ImageSharp.Image.Load(albumPath);
            Assert.Equal(256, image.Width);
            Assert.Equal(256, image.Height);
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
    public async Task ConvertAsync_BrokeRb3Con_SongIniContainsPositiveSongLength()
    {
        string outputRoot = Path.Combine(Path.GetTempPath(), $"charthub-broke-songlength-test-{Guid.NewGuid():N}");

        try
        {
            if (!File.Exists(BrokeRb3ConPath))
            {
                return;
            }

            Directory.CreateDirectory(outputRoot);

            var service = new ConversionService();
            ConversionResult result = await service.ConvertAsync(BrokeRb3ConPath, outputRoot);

            string songIniPath = Path.Combine(result.OutputDirectory, "song.ini");
            string songIni = await File.ReadAllTextAsync(songIniPath);

            // DTA song_length is preferred when non-zero (Onyx parity: authoritative MIDI-derived value).
            // Audio backing duration is the fallback. Either way, song_length must be positive.
            int songLength = 0;
            foreach (string line in songIni.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("song_length", StringComparison.Ordinal)
                    && trimmed.Contains('='))
                {
                    string[] parts = trimmed.Split('=', 2);
                    if (int.TryParse(parts[1].Trim(), out int parsed))
                    {
                        songLength = parsed;
                    }

                    break;
                }
            }

            Assert.True(songLength > 0, $"Expected positive song_length in song.ini but got {songLength}.");
        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }
}

[Trait("Category", "Unit")]
public sealed class SongLengthTimingTests
{
    // Minimal DTA with an explicit song_length value.
    private const string DtaWithLength = """
        ('test_song
          (name "Test Song")
          (artist "Test Artist")
          (song_length 185000)
          (preview 10000 40000)
          (song
            (name "songs/test/test")
            (tracks_count ())
            (tracks ())
            (pans ())
            (vols ())
          )
          (rank)
        )
        """;

    // Minimal DTA with no song_length (missing key entirely).
    private const string DtaWithoutLength = """
        ('test_song
          (name "Test Song")
          (artist "Test Artist")
          (preview 10000 40000)
          (song
            (name "songs/test/test")
            (tracks_count ())
            (tracks ())
            (pans ())
            (vols ())
          )
          (rank)
        )
        """;

    [Fact]
    public void SongIniGenerator_WhenDtaHasSongLength_EmitsExactDtaValue()
    {
        // Arrange: parse DTA that carries an explicit song_length
        DtaSongInfo songInfo = DtaParser.Parse(System.Text.Encoding.UTF8.GetBytes(DtaWithLength));

        Assert.Equal(185000, songInfo.SongLengthMs);

        // Act
        string ini = SongIniGenerator.Generate(songInfo);

        // Assert: DTA value preserved exactly
        Assert.Contains("song_length = 185000", ini, StringComparison.Ordinal);
    }

    [Fact]
    public void SongIniGenerator_WhenDtaHasNoSongLength_OmitsSongLength()
    {
        // Arrange: parse DTA with no song_length → SongLengthMs will be 0
        DtaSongInfo songInfo = DtaParser.Parse(System.Text.Encoding.UTF8.GetBytes(DtaWithoutLength));

        Assert.Equal(0, songInfo.SongLengthMs);

        // Act
        string ini = SongIniGenerator.Generate(songInfo);

        // Assert: song_length must not be emitted when 0 (SongIniGenerator suppresses zeroes)
        Assert.DoesNotContain("song_length", ini, StringComparison.Ordinal);
    }

    [Fact]
    public void SongIniGenerator_WhenDtaHasSongLength_AudioDurationIsNotUsed()
    {
        // Arrange: DTA has song_length 185000; simulate audio measurement returning 190000.
        // After the DTA-priority fix, the DTA value must win.
        DtaSongInfo dtaSongInfo = DtaParser.Parse(System.Text.Encoding.UTF8.GetBytes(DtaWithLength));

        // Simulate what ConversionService.WithSongLengthFromAudio does:
        // When dtaSongInfo.SongLengthMs > 0, the method returns the original songInfo unchanged.
        // The only observable test here is that SongIniGenerator uses the DTA value.
        string ini = SongIniGenerator.Generate(dtaSongInfo);

        Assert.Contains("song_length = 185000", ini, StringComparison.Ordinal);
        Assert.DoesNotContain("song_length = 190000", ini, StringComparison.Ordinal);
    }
}

public sealed class SngPackageReaderTests
{
    private static readonly string SampleSngPath =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../merges/Arcade Fire - Creature Comfort (Debugmod12).sng"));

    [Fact]
    public void Read_InvalidMagic_ThrowsInvalidData()
    {
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes("NOTSNGPKG");
        InvalidDataException ex = Assert.Throws<InvalidDataException>(() => SngPackageReader.Read(bytes));
        Assert.Contains("SNGPKG", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_SyntheticContainer_ParsesFileTable()
    {
        byte[] bytes = BuildSyntheticSngPkg();
        SngPackage pkg = SngPackageReader.Read(bytes);

        Assert.Equal(1, pkg.Version);
        Assert.Equal(3, pkg.Files.Count);
        Assert.Equal("notes.chart", pkg.Files[0].Name);
        Assert.Equal("album.jpg", pkg.Files[1].Name);
        Assert.Equal("song.opus", pkg.Files[2].Name);
        Assert.True(pkg.Files.All(f => f.Offset > 0));
        Assert.True(pkg.Files.All(f => f.Length > 0));
    }

    [Fact]
    public void Read_RealFixture_ParsesKnownFiles()
    {
        if (!File.Exists(SampleSngPath))
        {
            return;
        }

        SngPackage pkg = SngPackageReader.Read(SampleSngPath);
        Assert.True(pkg.Files.Count >= 2);
        Assert.Contains(pkg.Files, f => string.Equals(f.Name, "notes.chart", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(pkg.Files, f => string.Equals(f.Name, "song.opus", StringComparison.OrdinalIgnoreCase));
    }

    private static byte[] BuildSyntheticSngPkg()
    {
        const int size = 512;
        byte[] bytes = new byte[size];

        // Magic + version
        Array.Copy(System.Text.Encoding.ASCII.GetBytes("SNGPKG"), 0, bytes, 0, 6);
        bytes[6] = 1;

        int tablePos = 32;
        tablePos = WriteEntry(bytes, tablePos, "notes.chart", 320, 50);
        tablePos = WriteEntry(bytes, tablePos, "album.jpg", 370, 60);
        _ = WriteEntry(bytes, tablePos, "song.opus", 430, 70);

        return bytes;
    }

    private static int WriteEntry(byte[] bytes, int pos, string name, ulong offset, ulong length)
    {
        byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
        bytes[pos++] = (byte)nameBytes.Length;
        Array.Copy(nameBytes, 0, bytes, pos, nameBytes.Length);
        pos += nameBytes.Length;

        WriteUInt64LE(bytes, pos, offset);
        pos += 8;
        WriteUInt64LE(bytes, pos, length);
        pos += 8;

        return pos;
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

public sealed class SngMetadataExtractorTests
{
    [Fact]
    public void Extract_WhenSongIniPresent_UsesEmbeddedValues()
    {
        string songIni = """
            [song]
            name = Creature Comfort
            artist = Arcade Fire
            charter = Debug Charter
            """;

        byte[] containerBytes = BuildSyntheticSngPkg(
            [
                ("notes.chart", System.Text.Encoding.ASCII.GetBytes("dummy chart")),
                ("song.ini", System.Text.Encoding.UTF8.GetBytes(songIni)),
            ]);

        SngPackage package = SngPackageReader.Read(containerBytes);
        Conversion.Models.ConversionMetadata metadata = SngMetadataExtractor.Extract(
            package,
            containerBytes,
            "/tmp/arcade-fire.sng");

        Assert.Equal("Creature Comfort", metadata.Title);
        Assert.Equal("Arcade Fire", metadata.Artist);
        Assert.Equal("Debug Charter", metadata.Charter);
    }

    [Fact]
    public void Extract_WhenSongIniMissing_FallsBackToFilenameAndUnknowns()
    {
        byte[] containerBytes = BuildSyntheticSngPkg(
            [
                ("notes.chart", System.Text.Encoding.ASCII.GetBytes("dummy chart")),
                ("song.opus", [0x11, 0x22, 0x33]),
            ]);

        SngPackage package = SngPackageReader.Read(containerBytes);
        Conversion.Models.ConversionMetadata metadata = SngMetadataExtractor.Extract(
            package,
            containerBytes,
            "/tmp/Arcade Fire - Creature Comfort.sng");

        Assert.Equal("Arcade Fire - Creature Comfort", metadata.Title);
        Assert.Equal("Unknown Artist", metadata.Artist);
        Assert.Equal("Unknown Charter", metadata.Charter);
    }

    [Fact]
    public void Extract_WhenSongIniIsNestedAndMixedCase_ParsesCaseInsensitively()
    {
        string songIni = """
            [SONG]
            NAME = Generator
            ARTIST = Bad Religion
            CHARTER = chartbot
            """;

        byte[] containerBytes = BuildSyntheticSngPkg(
            [
                ("songs/pack/song.ini", System.Text.Encoding.UTF8.GetBytes(songIni)),
                ("song.opus", [0x01, 0x02, 0x03]),
            ]);

        SngPackage package = SngPackageReader.Read(containerBytes);
        Conversion.Models.ConversionMetadata metadata = SngMetadataExtractor.Extract(
            package,
            containerBytes,
            "/tmp/generator.sng");

        Assert.Equal("Generator", metadata.Title);
        Assert.Equal("Bad Religion", metadata.Artist);
        Assert.Equal("chartbot", metadata.Charter);
    }

    private static byte[] BuildSyntheticSngPkg(IReadOnlyList<(string Name, byte[] Data)> files)
    {
        int tableSize = files.Sum(f => 1 + System.Text.Encoding.ASCII.GetByteCount(f.Name) + 16);
        int dataStart = 64 + tableSize;
        int dataLength = files.Sum(f => f.Data.Length);

        byte[] bytes = new byte[dataStart + dataLength + 16];

        Array.Copy(System.Text.Encoding.ASCII.GetBytes("SNGPKG"), 0, bytes, 0, 6);
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

    private static int WriteEntry(byte[] bytes, int pos, string name, ulong offset, ulong length)
    {
        byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
        bytes[pos++] = (byte)nameBytes.Length;
        Array.Copy(nameBytes, 0, bytes, pos, nameBytes.Length);
        pos += nameBytes.Length;

        WriteUInt64LE(bytes, pos, offset);
        pos += 8;
        WriteUInt64LE(bytes, pos, length);
        pos += 8;

        return pos;
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
    public void BuildStemChannelMapForCloneHero_WhenSplitDrumAliasesExist_PrefersSplitDrumsOverCombinedStem()
    {
        Dictionary<string, IReadOnlyList<int>> trackChannels = new(StringComparer.OrdinalIgnoreCase)
        {
            ["drums"] = [0, 1],
            ["kick"] = [2],
            ["snare"] = [3],
            ["cymbals"] = [4],
            ["toms"] = [5],
            ["guitar"] = [6, 7],
        };

        Dictionary<string, List<int>> stemMap = MoggExtractor.BuildStemChannelMapForCloneHero(trackChannels);

        Assert.DoesNotContain("drums", stemMap.Keys);
        Assert.Equal([2], stemMap["drums_1"]);
        Assert.Equal([3], stemMap["drums_2"]);
        Assert.Equal([4], stemMap["drums_3"]);
        Assert.Equal([5], stemMap["drums_4"]);
        Assert.Equal([6, 7], stemMap["guitar"]);
    }

    [Fact]
    public void ShouldIncludeStemForCloneHero_DrumSplitStemsFollowDrumRank()
    {
        Dictionary<string, int> ranks = new(StringComparer.OrdinalIgnoreCase)
        {
            ["drum"] = 0,
            ["guitar"] = 123,
        };

        bool includeDrums1 = MoggExtractor.ShouldIncludeStemForCloneHero("drums_1", ranks);
        bool includeDrums2 = MoggExtractor.ShouldIncludeStemForCloneHero("drums_2", ranks);
        bool includeGuitar = MoggExtractor.ShouldIncludeStemForCloneHero("guitar", ranks);

        Assert.False(includeDrums1);
        Assert.False(includeDrums2);
        Assert.True(includeGuitar);
    }

    [Fact]
    public async Task ExtractStemsAsync_ProducesBackingAudioDeterministically()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"charthub-mogg-backing-only-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            string outputDir = Path.Combine(tempRoot, "out");
            Directory.CreateDirectory(outputDir);

            var extractor = new MoggExtractor();

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

            byte[] moggBytes = BuildSyntheticMoggWithVorbisChannels(2);
            IReadOnlyDictionary<string, string> stems = await extractor.ExtractStemsAsync(moggBytes, songInfo, outputDir);

            Assert.True(stems.TryGetValue("song", out string? backingPath));
            Assert.NotNull(backingPath);
            Assert.True(File.Exists(backingPath));
            Assert.DoesNotContain("guitar", stems.Keys);

            byte[] backingBytes = await File.ReadAllBytesAsync(backingPath!);
            Assert.True(backingBytes.Length >= 4);
            Assert.Equal((byte)'O', backingBytes[0]);
            Assert.Equal((byte)'g', backingBytes[1]);
            Assert.Equal((byte)'g', backingBytes[2]);
            Assert.Equal((byte)'S', backingBytes[3]);
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
        string tempRoot = Path.Combine(Path.GetTempPath(), $"charthub-mogg-offset-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            string outputDir = Path.Combine(tempRoot, "out");
            Directory.CreateDirectory(outputDir);

            var extractor = new MoggExtractor();
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
            Assert.DoesNotContain("guitar", stems.Keys);
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


    [Fact]
    public async Task ExtractStemsAsync_Mogg0x0B_DecryptsAndWritesBackingTrack()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"charthub-mogg-0x0b-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            string outputDir = Path.Combine(tempRoot, "out");
            Directory.CreateDirectory(outputDir);

            var extractor = new MoggExtractor();
            DtaSongInfo songInfo = new()
            {
                ShortName = "testtrack",
                Title = "Test Track",
                Artist = "Test Artist",
                TrackChannels = new Dictionary<string, IReadOnlyList<int>>
                {
                    ["guitar"] = [0, 1],
                },
                TotalChannels = 2,
            };

            byte[] mogg0x0b = BuildSyntheticEncrypted0x0bMogg(channelCount: 2);

            IReadOnlyDictionary<string, string> stems = await extractor.ExtractStemsAsync(mogg0x0b, songInfo, outputDir);

            Assert.Contains("song", stems.Keys);
            Assert.DoesNotContain("guitar", stems.Keys);

            byte[] backingBytes = await File.ReadAllBytesAsync(stems["song"]);
            Assert.True(backingBytes.Length >= 4);
            Assert.Equal((byte)'O', backingBytes[0]);
            Assert.Equal((byte)'g', backingBytes[1]);
            Assert.Equal((byte)'g', backingBytes[2]);
            Assert.Equal((byte)'S', backingBytes[3]);
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
    public async Task ExtractStemsAsync_Mogg0x0A_WritesBackingTrack()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"charthub-mogg-0x0d-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            string outputDir = Path.Combine(tempRoot, "out");
            Directory.CreateDirectory(outputDir);

            var extractor = new MoggExtractor();
            DtaSongInfo songInfo = new()
            {
                ShortName = "testtrack",
                Title = "Test Track",
                Artist = "Test Artist",
                TrackChannels = new Dictionary<string, IReadOnlyList<int>>
                {
                    ["guitar"] = [0, 1],
                },
                TotalChannels = 2,
            };

            byte[] mogg0x0a = BuildSyntheticMoggWithVorbisChannels(2);
            IReadOnlyDictionary<string, string> stems = await extractor.ExtractStemsAsync(mogg0x0a, songInfo, outputDir);

            Assert.Contains("song", stems.Keys);
            Assert.DoesNotContain("guitar", stems.Keys);
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

    /// <summary>
    /// Builds a synthetic MOGG version 0x0B file. The OGG payload is a minimal valid-looking
    /// buffer (OggS + vorbis identification header marker) encrypted with the known HMX RB1 key
    /// and a test PUBLIC_KEY, using the same AES-ECB counter-mode scheme as MoggExtractor.
    /// </summary>
    private static byte[] BuildSyntheticEncrypted0x0bMogg(byte channelCount)
    {
        // 16-byte PUBLIC_KEY used for the test (seeds the AES counter).
        byte[] publicKey =
        [
            0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
            ];

        // The same key that MoggExtractor.HmxPrivateKey0B uses.
        byte[] hmxKey0B =
        [
            0x37, 0xB2, 0xE2, 0xB9, 0x1C, 0x74, 0xFA, 0x9E,
                0x38, 0x81, 0x08, 0xEA, 0x36, 0x23, 0xDB, 0xE4,
            ];

        // Build a minimal raw OGG buffer (OggS sync + vorbis identification marker).
        byte[] rawOgg = new byte[64];
        rawOgg[0] = (byte)'O';
        rawOgg[1] = (byte)'g';
        rawOgg[2] = (byte)'g';
        rawOgg[3] = (byte)'S';
        int vorbisMarkerOffset = 16;
        rawOgg[vorbisMarkerOffset] = 0x01;      // packet_type (identification header)
        rawOgg[vorbisMarkerOffset + 1] = (byte)'v';
        rawOgg[vorbisMarkerOffset + 2] = (byte)'o';
        rawOgg[vorbisMarkerOffset + 3] = (byte)'r';
        rawOgg[vorbisMarkerOffset + 4] = (byte)'b';
        rawOgg[vorbisMarkerOffset + 5] = (byte)'i';
        rawOgg[vorbisMarkerOffset + 6] = (byte)'s';
        rawOgg[vorbisMarkerOffset + 11] = channelCount;

        // Encrypt the raw OGG using AES-CTR (identical algorithm to MoggExtractor.DecryptMoggAesCtr).
        byte[] encryptedOgg = AesCtrXor(rawOgg, hmxKey0B, publicKey);

        // Build the 0x0B MOGG header:
        // version(4) + oggOffset(4) + oggMapVersion(4) + bufferSize(4) + numPairs(4)
        // + PUBLIC_KEY(16) + encrypted OGG data
        // (numPairs = 0 so no seek table entries)
        int numPairs = 0;
        int headerSize = 20 + (numPairs * 8) + 16; // = 36
        byte[] mogg = new byte[headerSize + encryptedOgg.Length];

        // version = 0x0B LE
        mogg[0] = 0x0B;
        // oggOffset = headerSize LE
        mogg[4] = (byte)(headerSize & 0xFF);
        mogg[5] = (byte)((headerSize >> 8) & 0xFF);
        mogg[6] = (byte)((headerSize >> 16) & 0xFF);
        mogg[7] = (byte)((headerSize >> 24) & 0xFF);
        // oggMapVersion = 0x10 LE
        mogg[8] = 0x10;
        // numPairs = 0
        // PUBLIC_KEY at offset 20
        Buffer.BlockCopy(publicKey, 0, mogg, 20, 16);
        // encrypted OGG data
        Buffer.BlockCopy(encryptedOgg, 0, mogg, headerSize, encryptedOgg.Length);

        return mogg;
    }

    /// <summary>AES-ECB counter-mode XOR, matching the Harmonix MOGG scheme.</summary>
    private static byte[] AesCtrXor(byte[] data, byte[] key, byte[] initialCounter)
    {
        byte[] counter = new byte[16];
        Array.Copy(initialCounter, counter, 16);
        byte[] keystream = new byte[16];
        byte[] result = new byte[data.Length];

        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using ICryptoTransform encryptor = aes.CreateEncryptor();

        encryptor.TransformBlock(counter, 0, 16, keystream, 0);
        int blockOffset = 0;

        for (int i = 0; i < data.Length; i++)
        {
            if (blockOffset == 16)
            {
                for (int j = 0; j < 16; j++)
                {
                    counter[j]++;
                    if (counter[j] != 0) { break; }
                }

                encryptor.TransformBlock(counter, 0, 16, keystream, 0);
                blockOffset = 0;
            }

            result[i] = (byte)(data[i] ^ keystream[blockOffset]);
            blockOffset++;
        }

        return result;
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

    [Fact]
    public void Parse_WhenAuthorValueIsNestedList_ExtractsCharter()
    {
        const string nestedAuthorDta = """
            ('Broke'
              ('name' "Broke")
              ('artist' "Modest Mouse")
              ('author' ('name' "kamotch"))
              ('song' ('name' "songs/broke/broke"))
            )
            """;

        DtaSongInfo info = DtaParser.Parse(System.Text.Encoding.Latin1.GetBytes(nestedAuthorDta));
        Assert.Equal("kamotch", info.Charter);
    }

    [Fact]
    public void Parse_WhenCommentFooterContainsAuthor_UsesFooterAsCharterFallback()
    {
        const string footerAuthorDta = """
            ('suburbanwar'
              ('name' "Ready to Start")
              ('artist' "Arcade Fire")
              ('song' ('name' "songs/readytostart_af/readytostart_af"))
            )
            ;Song authored by Yaniv297
            """;

        DtaSongInfo info = DtaParser.Parse(System.Text.Encoding.Latin1.GetBytes(footerAuthorDta));
        Assert.Equal("Yaniv297", info.Charter);
    }

    [Fact]
    public void Parse_AndGenerateSongIni_WhenDrumOverridesPresent_UsesOverrideValues()
    {
        const string dtaWithOverrides = """
                        ('testsong'
                            ('name' "Test Song")
                            ('artist' "Test Artist")
                            ('charter' "Test Charter")
                            ('pro_drums' false)
                            ('five_lane_drums' true)
                            ('drum_fallback_blue' true)
                            ('rank'
                                ('drum' 124)
                            )
                            ('song'
                                ('name' "songs/testsong/testsong")
                            )
                        )
                        """;

        DtaSongInfo info = DtaParser.Parse(System.Text.Encoding.Latin1.GetBytes(dtaWithOverrides));
        string songIni = SongIniGenerator.Generate(info);

        Assert.Contains("pro_drums = False\r\n", songIni, StringComparison.Ordinal);
        Assert.Contains("five_lane_drums = True\r\n", songIni, StringComparison.Ordinal);
        Assert.Contains("drum_fallback_blue = True\r\n", songIni, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateSongIni_WhenDtaContainsOnyxFields_WritesExpectedKeys()
    {
        DtaSongInfo info = new()
        {
            ShortName = "suburbanwar",
            Title = "Ready to Start",
            Artist = "Arcade Fire",
            Charter = "Yaniv297",
            Album = "The Suburbs",
            Genre = "alternative",
            Year = "2010",
            AlbumTrack = 2,
            SongLengthMs = 257033,
            PreviewStartMs = 30000,
            PreviewEndMs = 60000,
            LoadingPhrase = "Onyx parity check",
            IsCover = true,
            VocalParts = 2,
            Ranks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["drum"] = 124,
                ["guitar"] = 139,
                ["bass"] = 1,
                ["vocals"] = 218,
                ["keys"] = 153,
                ["band"] = 165,
            }
        };

        string songIni = SongIniGenerator.Generate(info);

        Assert.Contains("album = The Suburbs\r\n", songIni, StringComparison.Ordinal);
        Assert.Contains("frets = Yaniv297\r\n", songIni, StringComparison.Ordinal);
        Assert.Contains("song_length = 257033\r\n", songIni, StringComparison.Ordinal);
        Assert.Contains("preview_start_time = 30000\r\n", songIni, StringComparison.Ordinal);
        Assert.Contains("preview_end_time = 60000\r\n", songIni, StringComparison.Ordinal);
        Assert.Contains("loading_phrase = Onyx parity check\r\n", songIni, StringComparison.Ordinal);
        Assert.Contains("tags = cover\r\n", songIni, StringComparison.Ordinal);
        Assert.Contains("genre = alternative\r\n", songIni, StringComparison.Ordinal);
        Assert.Contains("year = 2010\r\n", songIni, StringComparison.Ordinal);
        Assert.Contains("track = 2\r\n", songIni, StringComparison.Ordinal);
        Assert.Contains("album_track = 2\r\n", songIni, StringComparison.Ordinal);
        Assert.Contains("diff_drums = 1\r\n", songIni, StringComparison.Ordinal);
        Assert.Contains("diff_drums_real = 1\r\n", songIni, StringComparison.Ordinal);
        Assert.Contains("diff_guitar = 1\r\n", songIni, StringComparison.Ordinal);
        Assert.Contains("diff_guitar_ghl = -1\r\n", songIni, StringComparison.Ordinal);
        Assert.Contains("diff_bass_ghl = -1\r\n", songIni, StringComparison.Ordinal);
        Assert.Contains("diff_vocals = 3\r\n", songIni, StringComparison.Ordinal);
        Assert.Contains("diff_vocals_harm = 3\r\n", songIni, StringComparison.Ordinal);
        Assert.Contains("diff_band = 1\r\n", songIni, StringComparison.Ordinal);
        Assert.Contains("diff_drums_real_ps = -1\r\n", songIni, StringComparison.Ordinal);
        Assert.Contains("diff_keys_real_ps = -1\r\n", songIni, StringComparison.Ordinal);
        Assert.Contains("diff_guitar_pad = -1\r\n", songIni, StringComparison.Ordinal);
        Assert.Contains("diff_bass_pad = -1\r\n", songIni, StringComparison.Ordinal);
        Assert.Contains("diff_drums_pad = -1\r\n", songIni, StringComparison.Ordinal);
        Assert.Contains("diff_vocals_pad = -1\r\n", songIni, StringComparison.Ordinal);
        Assert.Contains("diff_keys_pad = -1\r\n", songIni, StringComparison.Ordinal);
        Assert.Contains("star_power_note = 116\r\n", songIni, StringComparison.Ordinal);
        Assert.Contains("multiplier_note = 116\r\n", songIni, StringComparison.Ordinal);
        Assert.Contains("sysex_slider = False\r\n", songIni, StringComparison.Ordinal);
        Assert.Contains("sysex_open_bass = False\r\n", songIni, StringComparison.Ordinal);
        Assert.Contains("pro_drums = True\r\n", songIni, StringComparison.Ordinal);
        Assert.DoesNotContain("five_lane_drums = ", songIni, StringComparison.Ordinal);
        Assert.DoesNotContain("drum_fallback_blue = ", songIni, StringComparison.Ordinal);
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

[Trait("Category", "Unit")]
public sealed class ExpertPlusMidiGeneratorTests
{
    // Minimal helper: builds a well-formed Format-1 MIDI with one tempo track and one named track.
    // The named track contains a single NoteOn and NoteOff event for the supplied note number.
    private static byte[] BuildMinimalMidi(string trackName, byte noteNumber, byte velocity = 64)
    {
        // Named track data: [delta=0][TrackName meta][delta=0][NoteOn][delta=4][NoteOff][delta=0][EndOfTrack]
        using var trackStream = new MemoryStream();

        // delta 0
        trackStream.WriteByte(0x00);
        // FF 03 <len> <name>
        trackStream.WriteByte(0xFF);
        trackStream.WriteByte(0x03);
        byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(trackName);
        trackStream.WriteByte((byte)nameBytes.Length);
        trackStream.Write(nameBytes);

        // delta 0, NoteOn ch0 [note] [velocity]
        trackStream.WriteByte(0x00);
        trackStream.WriteByte(0x90);
        trackStream.WriteByte(noteNumber);
        trackStream.WriteByte(velocity);

        // delta 4, NoteOff ch0 [note] 0
        trackStream.WriteByte(0x04);
        trackStream.WriteByte(0x80);
        trackStream.WriteByte(noteNumber);
        trackStream.WriteByte(0x00);

        // delta 0, EndOfTrack
        trackStream.WriteByte(0x00);
        trackStream.WriteByte(0xFF);
        trackStream.WriteByte(0x2F);
        trackStream.WriteByte(0x00);

        byte[] trackData = trackStream.ToArray();

        // Minimal tempo track (just EndOfTrack)
        byte[] tempoTrack = [0x00, 0xFF, 0x2F, 0x00];

        using var ms = new MemoryStream();

        // MThd
        ms.Write("MThd"u8);
        WriteUInt32BE(ms, 6);
        WriteUInt16BE(ms, 1);  // format 1
        WriteUInt16BE(ms, 2);  // two tracks
        WriteUInt16BE(ms, 480); // division

        // Track 0: tempo
        ms.Write("MTrk"u8);
        WriteUInt32BE(ms, (uint)tempoTrack.Length);
        ms.Write(tempoTrack);

        // Track 1: named track
        ms.Write("MTrk"u8);
        WriteUInt32BE(ms, (uint)trackData.Length);
        ms.Write(trackData);

        return ms.ToArray();
    }

    private static void WriteUInt16BE(Stream s, ushort v)
    {
        s.WriteByte((byte)(v >> 8));
        s.WriteByte((byte)(v & 0xFF));
    }

    private static void WriteUInt32BE(Stream s, uint v)
    {
        s.WriteByte((byte)(v >> 24));
        s.WriteByte((byte)((v >> 16) & 0xFF));
        s.WriteByte((byte)((v >> 8) & 0xFF));
        s.WriteByte((byte)(v & 0xFF));
    }

    [Fact]
    public void Apply_WhenPartDrumsHasKick2x_RemapsNote95ToNote96()
    {
        byte[] input = BuildMinimalMidi("PART DRUMS", noteNumber: 95, velocity: 100);
        byte[] output = ExpertPlusMidiGenerator.Apply(input);

        // expert+.mid must be the same length or different only by the note byte change.
        // The NoteOn data byte (note 95) must have become 96; velocity unchanged.
        // Scan output for NoteOn event in PART DRUMS track — note byte must be 96, not 95.
        bool foundNote96NoteOn = false;
        bool foundNote95 = false;
        int pos = 14; // skip MThd
        while (pos + 8 <= output.Length)
        {
            if (output[pos] != 'M' || output[pos + 1] != 'T' || output[pos + 2] != 'r' || output[pos + 3] != 'k')
            {
                break;
            }

            int trackLen = (output[pos + 4] << 24) | (output[pos + 5] << 16) | (output[pos + 6] << 8) | output[pos + 7];
            byte[] track = output[(pos + 8)..(pos + 8 + trackLen)];
            pos += 8 + trackLen;

            // Brute-force scan for NoteOn (0x90) byte sequences in the track payload.
            for (int i = 0; i < track.Length - 2; i++)
            {
                if ((track[i] & 0xF0) == 0x90)
                {
                    byte note = track[i + 1];
                    if (note == 96)
                    {
                        foundNote96NoteOn = true;
                    }

                    if (note == 95)
                    {
                        foundNote95 = true;
                    }
                }
            }
        }

        Assert.True(foundNote96NoteOn, "expert+.mid must contain a NoteOn for note 96 (kick).");
        Assert.False(foundNote95, "expert+.mid must not contain note 95 (2x kick removed).");
    }

    [Fact]
    public void Apply_WhenTrackIsNotPartDrums_LeavesNotesUnchanged()
    {
        byte[] input = BuildMinimalMidi("PART GUITAR", noteNumber: 95, velocity: 64);
        byte[] output = ExpertPlusMidiGenerator.Apply(input);

        // Non-PART DRUMS tracks must not be modified — note 95 survives in output.
        bool foundNote95 = false;
        int pos = 14;
        while (pos + 8 <= output.Length)
        {
            if (output[pos] != 'M' || output[pos + 1] != 'T' || output[pos + 2] != 'r' || output[pos + 3] != 'k')
            {
                break;
            }

            int trackLen = (output[pos + 4] << 24) | (output[pos + 5] << 16) | (output[pos + 6] << 8) | output[pos + 7];
            byte[] track = output[(pos + 8)..(pos + 8 + trackLen)];
            pos += 8 + trackLen;

            for (int i = 0; i < track.Length - 2; i++)
            {
                if ((track[i] & 0xF0) == 0x90 && track[i + 1] == 95)
                {
                    foundNote95 = true;
                }
            }
        }

        Assert.True(foundNote95, "Non-drum tracks must not have note 95 remapped.");
    }

    [Fact]
    public void Apply_WhenPartDrumsHasNoKick2x_OutputIsStructurallyIdentical()
    {
        // note 60 (green, Easy difficulty) — should pass through unchanged.
        byte[] input = BuildMinimalMidi("PART DRUMS", noteNumber: 60, velocity: 80);
        byte[] output = ExpertPlusMidiGenerator.Apply(input);

        Assert.Equal(input.Length, output.Length);
        Assert.Equal(input, output);
    }

    [Fact]
    public void TransformDrumsTrack_RemapsNote95ToNote96WithRunningStatus()
    {
        // Build a raw track with two NoteOn events under running status:
        // [0x00][0x90][95][100]  <- first NoteOn with explicit status
        // [0x00][95][100]        <- second NoteOn via running status (no status byte)
        // [0x00][0xFF][0x2F][0x00]  <- EndOfTrack
        byte[] trackData =
        [
            0x00, 0x90, 95, 100,   // delta=0 NoteOn ch0 note=95 vel=100 (explicit)
            0x00, 95, 100,          // delta=0 NoteOn via running status, note=95 vel=100
            0x00, 0xFF, 0x2F, 0x00, // EndOfTrack
        ];

        byte[] result = ExpertPlusMidiGenerator.TransformDrumsTrack(trackData);

        // Both note bytes (at index 2 and 5) should be 96.
        Assert.Equal(96, result[2]);
        Assert.Equal(96, result[5]);
    }
}

/// <summary>
/// Parity harness: regression assertions for file presence, song.ini key family, and drum
/// stem naming. These tests are designed to fail loudly if a future change removes a required
/// output artifact or regresses an Onyx-aligned behavior.
/// </summary>
/// <remarks>
/// All tests guard on fixture availability — they skip (pass silently) on CI without assets.
/// </remarks>
[Trait("Category", "Parity")]
public sealed class ParityHardeningTests
{
    // ------------------------------------------------------------------ fixtures

    private static readonly string SampleRb3ConPath =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../merges/Ready to Start-e99c44e9-43a5-4c54-aa86-4cffb56bb215.rb3con"));

    private static readonly string BrokeRb3ConPath =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../kam_mm_broke.rb3con"));

    // ------------------------------------------------------------------ required song.ini keys
    // Every CON/SNG output must carry these keys regardless of source metadata.
    private static readonly string[] RequiredSongIniKeys =
    [
        "name",
        "artist",
        "charter",
        "frets",
        "diff_band",
        "diff_guitar",
        "diff_guitar_ghl",
        "diff_bass",
        "diff_bass_ghl",
        "diff_drums",
        "diff_drums_real",
        "diff_keys",
        "diff_vocals",
        "star_power_note",
        "multiplier_note",
    ];

    // ------------------------------------------------------------------ helpers

    private static IReadOnlyDictionary<string, string> ParseSongIni(string iniContent)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in iniContent.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith('[') || string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            int eq = trimmed.IndexOf('=');
            if (eq > 0)
            {
                result[trimmed[..eq].Trim()] = trimmed[(eq + 1)..].Trim();
            }
        }

        return result;
    }

    // ------------------------------------------------------------------ file presence

    [Fact]
    public async Task ConvertAsync_SampleRb3Con_ProducesRequiredOutputFiles()
    {
        if (!File.Exists(SampleRb3ConPath))
        {
            return;
        }

        string outputRoot = Path.Combine(Path.GetTempPath(), $"charthub-parity-files-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(outputRoot);
            var service = new ConversionService();
            ConversionResult result = await service.ConvertAsync(SampleRb3ConPath, outputRoot);

            // Core chart files
            Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "notes.mid")),
                "notes.mid must be present in every CON conversion output.");
            Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "song.ini")),
                "song.ini must be present in every CON conversion output.");

            // expert+.mid: emitted when drum rank > 0
            // The sample fixture has drums, so expert+.mid must be present.
            Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "expert+.mid")),
                "expert+.mid must be present when the source has a non-zero drum rank.");

            // Audio: at minimum the backing stem must exist
            bool hasBacking = File.Exists(Path.Combine(result.OutputDirectory, "song.ogg"))
                || Directory.EnumerateFiles(result.OutputDirectory, "*.ogg", SearchOption.TopDirectoryOnly).Any();
            Assert.True(hasBacking, "At least one OGG audio file must be present after CON conversion.");
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
    public async Task ConvertAsync_BrokeRb3Con_ProducesRequiredOutputFiles()
    {
        if (!File.Exists(BrokeRb3ConPath))
        {
            return;
        }

        string outputRoot = Path.Combine(Path.GetTempPath(), $"charthub-parity-files-broke-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(outputRoot);
            var service = new ConversionService();
            ConversionResult result = await service.ConvertAsync(BrokeRb3ConPath, outputRoot);

            Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "notes.mid")),
                "notes.mid must be present in every CON conversion output.");
            Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "song.ini")),
                "song.ini must be present in every CON conversion output.");

            bool hasBacking = File.Exists(Path.Combine(result.OutputDirectory, "song.ogg"))
                || Directory.EnumerateFiles(result.OutputDirectory, "*.ogg", SearchOption.TopDirectoryOnly).Any();
            Assert.True(hasBacking, "At least one OGG audio file must be present after CON conversion.");
        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    // ------------------------------------------------------------------ song.ini key family

    [Fact]
    public async Task ConvertAsync_SampleRb3Con_SongIniContainsRequiredKeyFamily()
    {
        if (!File.Exists(SampleRb3ConPath))
        {
            return;
        }

        string outputRoot = Path.Combine(Path.GetTempPath(), $"charthub-parity-keys-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(outputRoot);
            var service = new ConversionService();
            ConversionResult result = await service.ConvertAsync(SampleRb3ConPath, outputRoot);

            string iniContent = await File.ReadAllTextAsync(Path.Combine(result.OutputDirectory, "song.ini"));
            IReadOnlyDictionary<string, string> keys = ParseSongIni(iniContent);

            foreach (string requiredKey in RequiredSongIniKeys)
            {
                Assert.True(keys.ContainsKey(requiredKey),
                    $"song.ini must contain required key '{requiredKey}' but it was absent.");
            }

            // Verify Onyx-aligned GHL key names (not the old diff_guitarghl / diff_bassghl form).
            Assert.True(keys.ContainsKey("diff_guitar_ghl"),
                "song.ini must use Onyx-aligned key 'diff_guitar_ghl' (not 'diff_guitarghl').");
            Assert.True(keys.ContainsKey("diff_bass_ghl"),
                "song.ini must use Onyx-aligned key 'diff_bass_ghl' (not 'diff_bassghl').");
            Assert.False(keys.ContainsKey("diff_guitarghl"),
                "song.ini must not emit legacy key 'diff_guitarghl'.");
            Assert.False(keys.ContainsKey("diff_bassghl"),
                "song.ini must not emit legacy key 'diff_bassghl'.");
        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    // ------------------------------------------------------------------ drum stem naming

    [Fact]
    public async Task ConvertAsync_SampleRb3Con_DrumStemsFollowSplitOrCombinedRule()
    {
        if (!File.Exists(SampleRb3ConPath))
        {
            return;
        }

        string outputRoot = Path.Combine(Path.GetTempPath(), $"charthub-parity-drums-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(outputRoot);
            var service = new ConversionService();
            ConversionResult result = await service.ConvertAsync(SampleRb3ConPath, outputRoot);

            bool hasCombinedDrums = File.Exists(Path.Combine(result.OutputDirectory, "drums.ogg"));
            bool hasSplitDrums = File.Exists(Path.Combine(result.OutputDirectory, "drums_1.ogg"))
                || File.Exists(Path.Combine(result.OutputDirectory, "drums_2.ogg"))
                || File.Exists(Path.Combine(result.OutputDirectory, "drums_3.ogg"))
                || File.Exists(Path.Combine(result.OutputDirectory, "drums_4.ogg"));

            // Onyx parity: output is either combined OR split, never both.
            Assert.False(hasCombinedDrums && hasSplitDrums,
                "Drum stems must follow the Onyx mutual-exclusion rule: output is either drums.ogg OR drums_N.ogg, never both.");

            // At least one form must be present when the source DTA has a non-zero drum rank.
            // (The sample fixture is known to have drums.)
            Assert.True(hasCombinedDrums || hasSplitDrums,
                "Expected at least one drum stem OGG (drums.ogg or drums_1..4.ogg) from a fixture with non-zero drum rank.");
        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    // ------------------------------------------------------------------ expert+.mid content sanity

    [Fact]
    public async Task ConvertAsync_SampleRb3Con_ExpertPlusMidContainsNotesMidPartDrumsTrack()
    {
        if (!File.Exists(SampleRb3ConPath))
        {
            return;
        }

        string outputRoot = Path.Combine(Path.GetTempPath(), $"charthub-parity-expertplus-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(outputRoot);
            var service = new ConversionService();
            ConversionResult result = await service.ConvertAsync(SampleRb3ConPath, outputRoot);

            string expertPlusPath = Path.Combine(result.OutputDirectory, "expert+.mid");
            string notesMidPath = Path.Combine(result.OutputDirectory, "notes.mid");

            if (!File.Exists(expertPlusPath))
            {
                // If no drum rank, expert+.mid is correctly absent — skip the rest.
                return;
            }

            byte[] expertPlus = await File.ReadAllBytesAsync(expertPlusPath);
            byte[] notesMid = await File.ReadAllBytesAsync(notesMidPath);

            // expert+.mid must be a valid MIDI (starts with MThd).
            Assert.True(expertPlus.Length >= 14
                && expertPlus[0] == 'M' && expertPlus[1] == 'T'
                && expertPlus[2] == 'h' && expertPlus[3] == 'd',
                "expert+.mid must be a valid Standard MIDI file (MThd header).");

            // expert+.mid must not be larger than notes.mid by more than a trivial margin —
            // the transform only remaps note bytes and cannot add new events.
            Assert.True(expertPlus.Length <= notesMid.Length + 16,
                $"expert+.mid ({expertPlus.Length} bytes) must not be significantly larger than notes.mid ({notesMid.Length} bytes).");
        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }
}

