using System.Text;

using ChartHub.Conversion.Sng;

namespace ChartHub.Conversion.Tests;

public sealed class SngAudioExtractorTests
{
    private static readonly string AudioFixturePath =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../merges/Nine Inch Nails - The Perfect Drug (Harmonix).sng"));

    [Fact]
    public async Task ExtractAsync_WhenAudioEntriesExist_WritesAllSupportedAudioFiles()
    {
        byte[] containerBytes = BuildSyntheticSngPkg(
            [
                ("song.opus", BuildOggLikeBytes("OpusHead-main")),
                ("guitar.opus", BuildOggLikeBytes("OpusHead-gtr")),
                ("album.jpg", [0x01, 0x02, 0x03]),
            ]);

        SngPackage package = SngPackageReader.Read(containerBytes);
        string outputRoot = Path.Combine(Path.GetTempPath(), $"charthub-sng-audio-{Guid.NewGuid():N}");

        try
        {
            IReadOnlyList<string> written = await SngAudioExtractor.ExtractAsync(package, containerBytes, outputRoot);

            Assert.Equal(2, written.Count);
            Assert.Contains(written, path => Path.GetFileName(path).Equals("song.opus", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(written, path => Path.GetFileName(path).Equals("guitar.opus", StringComparison.OrdinalIgnoreCase));
            Assert.True(File.Exists(Path.Combine(outputRoot, "song.opus")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "guitar.opus")));
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
    public async Task ExtractAsync_WhenNoAudioEntriesExist_ThrowsInvalidData()
    {
        byte[] containerBytes = BuildSyntheticSngPkg(
            [
                ("notes.mid", [0x01, 0x02, 0x03]),
                ("song.ini", Encoding.UTF8.GetBytes("[song]\nname = Example")),
            ]);

        SngPackage package = SngPackageReader.Read(containerBytes);
        string outputRoot = Path.Combine(Path.GetTempPath(), $"charthub-sng-audio-{Guid.NewGuid():N}");

        try
        {
            InvalidDataException ex = await Assert.ThrowsAsync<InvalidDataException>(
                () => SngAudioExtractor.ExtractAsync(package, containerBytes, outputRoot));

            Assert.Contains("audio entries", ex.Message, StringComparison.OrdinalIgnoreCase);
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
    public async Task ExtractAsync_RealFixture_WritesSongAudio()
    {
        if (!File.Exists(AudioFixturePath))
        {
            return;
        }

        byte[] containerBytes = File.ReadAllBytes(AudioFixturePath);
        SngPackage package = SngPackageReader.Read(containerBytes);
        string outputRoot = Path.Combine(Path.GetTempPath(), $"charthub-sng-audio-{Guid.NewGuid():N}");

        try
        {
            IReadOnlyList<string> written = await SngAudioExtractor.ExtractAsync(package, containerBytes, outputRoot);

            Assert.Contains(written, path => Path.GetFileName(path).Equals("song.opus", StringComparison.OrdinalIgnoreCase));
            byte[] songBytes = await File.ReadAllBytesAsync(Path.Combine(outputRoot, "song.opus"));
            Assert.True(songBytes.Length >= 4);
            Assert.Equal((byte)'O', songBytes[0]);
            Assert.Equal((byte)'g', songBytes[1]);
            Assert.Equal((byte)'g', songBytes[2]);
            Assert.Equal((byte)'S', songBytes[3]);
        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
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

    private static byte[] BuildOggLikeBytes(string marker)
    {
        byte[] markerBytes = Encoding.ASCII.GetBytes(marker);
        byte[] bytes = new byte[4 + markerBytes.Length];
        bytes[0] = (byte)'O';
        bytes[1] = (byte)'g';
        bytes[2] = (byte)'g';
        bytes[3] = (byte)'S';
        Buffer.BlockCopy(markerBytes, 0, bytes, 4, markerBytes.Length);
        return bytes;
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