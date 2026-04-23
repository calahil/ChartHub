using System.Text;

using ChartHub.Conversion.Sng;

namespace ChartHub.Conversion.Tests;

public sealed class SngAlbumArtExtractorTests
{
    private static readonly string AlbumArtFixturePath =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../merges/Nine Inch Nails - The Perfect Drug (Harmonix).sng"));

    [Fact]
    public async Task ExtractAsync_WhenPreferredAlbumEntryExists_WritesNormalizedAlbumFile()
    {
        byte[] expectedImageBytes = BuildJpegLikeBytes("preferred");
        byte[] containerBytes = BuildSyntheticSngPkg(
            [
                ("background.jpg", BuildJpegLikeBytes("background")),
                ("album.jpg", expectedImageBytes),
                ("song.opus", [0x01, 0x02, 0x03]),
            ]);

        SngPackage package = SngPackageReader.Read(containerBytes);
        string outputRoot = Path.Combine(Path.GetTempPath(), $"charthub-sng-art-{Guid.NewGuid():N}");

        try
        {
            string outputPath = await SngAlbumArtExtractor.ExtractAsync(package, containerBytes, outputRoot);

            Assert.Equal(Path.Combine(outputRoot, "album.jpg"), outputPath);
            Assert.True(File.Exists(outputPath));
            Assert.Equal(expectedImageBytes, await File.ReadAllBytesAsync(outputPath));
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
    public async Task ExtractAsync_WhenOnlyFallbackImageExists_WritesAlbumWithSourceExtension()
    {
        byte[] expectedImageBytes = BuildPngLikeBytes("fallback");
        byte[] containerBytes = BuildSyntheticSngPkg(
            [
                ("cover.png", expectedImageBytes),
                ("song.opus", [0x01, 0x02, 0x03]),
            ]);

        SngPackage package = SngPackageReader.Read(containerBytes);
        string outputRoot = Path.Combine(Path.GetTempPath(), $"charthub-sng-art-{Guid.NewGuid():N}");

        try
        {
            string outputPath = await SngAlbumArtExtractor.ExtractAsync(package, containerBytes, outputRoot);

            Assert.Equal(Path.Combine(outputRoot, "album.png"), outputPath);
            Assert.True(File.Exists(outputPath));
            Assert.Equal(expectedImageBytes, await File.ReadAllBytesAsync(outputPath));
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
    public async Task ExtractAsync_WhenNoImageEntriesExist_ThrowsInvalidData()
    {
        byte[] containerBytes = BuildSyntheticSngPkg(
            [
                ("notes.mid", [0x01, 0x02, 0x03]),
                ("song.opus", [0x04, 0x05, 0x06]),
            ]);

        SngPackage package = SngPackageReader.Read(containerBytes);
        string outputRoot = Path.Combine(Path.GetTempPath(), $"charthub-sng-art-{Guid.NewGuid():N}");

        try
        {
            InvalidDataException ex = await Assert.ThrowsAsync<InvalidDataException>(
                () => SngAlbumArtExtractor.ExtractAsync(package, containerBytes, outputRoot));

            Assert.Contains("album art", ex.Message, StringComparison.OrdinalIgnoreCase);
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
    public async Task ExtractAsync_RealFixture_WritesAlbumJpeg()
    {
        if (!File.Exists(AlbumArtFixturePath))
        {
            return;
        }

        byte[] containerBytes = await File.ReadAllBytesAsync(AlbumArtFixturePath);
        SngPackage package = SngPackageReader.Read(containerBytes);
        string outputRoot = Path.Combine(Path.GetTempPath(), $"charthub-sng-art-{Guid.NewGuid():N}");

        try
        {
            string outputPath = await SngAlbumArtExtractor.ExtractAsync(package, containerBytes, outputRoot);
            byte[] imageBytes = await File.ReadAllBytesAsync(outputPath);

            Assert.Equal(Path.Combine(outputRoot, "album.jpg"), outputPath);
            Assert.True(imageBytes.Length >= 3);
            Assert.Equal(0xFF, imageBytes[0]);
            Assert.Equal(0xD8, imageBytes[1]);
            Assert.Equal(0xFF, imageBytes[2]);
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

    private static byte[] BuildJpegLikeBytes(string marker)
    {
        byte[] markerBytes = Encoding.ASCII.GetBytes(marker);
        byte[] bytes = new byte[3 + markerBytes.Length];
        bytes[0] = 0xFF;
        bytes[1] = 0xD8;
        bytes[2] = 0xFF;
        Buffer.BlockCopy(markerBytes, 0, bytes, 3, markerBytes.Length);
        return bytes;
    }

    private static byte[] BuildPngLikeBytes(string marker)
    {
        byte[] markerBytes = Encoding.ASCII.GetBytes(marker);
        byte[] bytes = new byte[8 + markerBytes.Length];
        bytes[0] = 0x89;
        bytes[1] = (byte)'P';
        bytes[2] = (byte)'N';
        bytes[3] = (byte)'G';
        bytes[4] = 0x0D;
        bytes[5] = 0x0A;
        bytes[6] = 0x1A;
        bytes[7] = 0x0A;
        Buffer.BlockCopy(markerBytes, 0, bytes, 8, markerBytes.Length);
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