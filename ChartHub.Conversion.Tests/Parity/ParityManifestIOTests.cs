using System.Text;

namespace ChartHub.Conversion.Tests.Parity;

public sealed class ParityManifestIOTests
{
    [Fact]
    public void BuildChecksumsForDirectory_WhenNotesMidAndNotesChartPresent_PrefersNotesMidForParity()
    {
        string root = Path.Combine(Path.GetTempPath(), $"charthub-parity-io-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllBytes(Path.Combine(root, "notes.mid"), BuildMidiStub());
            File.WriteAllBytes(Path.Combine(root, "notes.chart"), Encoding.ASCII.GetBytes("chart"));
            File.WriteAllBytes(Path.Combine(root, "song.ini"), Encoding.ASCII.GetBytes("[song]\nname=Test"));

            IReadOnlyList<ParityChecksumFile> files = ParityManifestIO.BuildChecksumsForDirectory(root);

            Assert.Contains(files, file => string.Equals(file.Path, "notes.mid", StringComparison.Ordinal));
            Assert.DoesNotContain(files, file => string.Equals(file.Path, "notes.chart", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void BuildChecksumsForDirectory_WhenOnlyNotesChartPresent_KeepsNotesChart()
    {
        string root = Path.Combine(Path.GetTempPath(), $"charthub-parity-io-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllBytes(Path.Combine(root, "notes.chart"), Encoding.ASCII.GetBytes("chart"));

            IReadOnlyList<ParityChecksumFile> files = ParityManifestIO.BuildChecksumsForDirectory(root);

            Assert.Contains(files, file => string.Equals(file.Path, "notes.chart", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void BuildChecksumsForDirectory_SyntheticCanonicalPair_BothAndMidOnlyNormalizeEquivalent()
    {
        string root = Path.Combine(Path.GetTempPath(), $"charthub-parity-io-test-{Guid.NewGuid():N}");
        string bothDir = Path.Combine(root, "both");
        string midOnlyDir = Path.Combine(root, "mid-only");
        byte[] midi = BuildMidiStub();

        try
        {
            Directory.CreateDirectory(bothDir);
            Directory.CreateDirectory(midOnlyDir);

            File.WriteAllBytes(Path.Combine(bothDir, "notes.mid"), midi);
            File.WriteAllBytes(Path.Combine(bothDir, "notes.chart"), Encoding.ASCII.GetBytes("chart"));
            File.WriteAllBytes(Path.Combine(bothDir, "song.ini"), Encoding.ASCII.GetBytes("[song]\nname=Pair"));

            File.WriteAllBytes(Path.Combine(midOnlyDir, "notes.mid"), midi);
            File.WriteAllBytes(Path.Combine(midOnlyDir, "song.ini"), Encoding.ASCII.GetBytes("[song]\nname=Pair"));

            IReadOnlyList<ParityChecksumFile> bothFiles = ParityManifestIO.BuildChecksumsForDirectory(bothDir);
            IReadOnlyList<ParityChecksumFile> midOnlyFiles = ParityManifestIO.BuildChecksumsForDirectory(midOnlyDir);

            string[] bothPaths = bothFiles.Select(file => file.Path).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            string[] midOnlyPaths = midOnlyFiles.Select(file => file.Path).OrderBy(path => path, StringComparer.Ordinal).ToArray();

            Assert.Equal(midOnlyPaths, bothPaths);

            string bothMidHash = bothFiles.Single(file => string.Equals(file.Path, "notes.mid", StringComparison.Ordinal)).Sha256;
            string midOnlyHash = midOnlyFiles.Single(file => string.Equals(file.Path, "notes.mid", StringComparison.Ordinal)).Sha256;
            Assert.Equal(midOnlyHash, bothMidHash);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static byte[] BuildMidiStub()
    {
        return
        [
            (byte)'M', (byte)'T', (byte)'h', (byte)'d',
            0x00, 0x00, 0x00, 0x06,
            0x00, 0x01, 0x00, 0x01, 0x01, 0xE0,
            (byte)'M', (byte)'T', (byte)'r', (byte)'k',
            0x00, 0x00, 0x00, 0x04,
            0x00, 0xFF, 0x2F, 0x00,
        ];
    }
}
