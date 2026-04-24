using System.Diagnostics;
using System.Security.Cryptography;

using ChartHub.Conversion;
using ChartHub.Conversion.Models;

namespace ChartHub.Conversion.Tests;

/// <summary>
/// M6 non-functional staging gate suite.
/// Proves repeatability, SLO ceiling, allocation ceiling, and resilience for partial failures.
/// All tests use synthetic containers so they run without binary merge assets.
/// </summary>
public sealed class StagingGateRepeatabilityTests
{
    // SLO ceiling: synthetic SNG conversion must complete within this wall-time budget.
    // Set generously to tolerate CI variance while proving a hard upper bound exists.
    private const int SloWallTimeCeilingMs = 10_000;

    // Allocation ceiling: synthetic SNG conversion must not allocate more than this.
    private const long AllocationCeilingBytes = 256 * 1024 * 1024; // 256 MiB

    [Fact]
    public async Task ConvertAsync_SameSyntheticSng_ProducesIdenticalOutputChecksums()
    {
        byte[] container = BuildMinimalSng("Repeatability Artist", "Repeatability Song", "tester");
        string outputA = Path.Combine(Path.GetTempPath(), $"charthub-repeat-a-{Guid.NewGuid():N}");
        string outputB = Path.Combine(Path.GetTempPath(), $"charthub-repeat-b-{Guid.NewGuid():N}");

        try
        {
            string inputPath = await WriteTempSngAsync(container);

            try
            {
                Directory.CreateDirectory(outputA);
                Directory.CreateDirectory(outputB);

                var service = new ConversionService();
                ConversionResult resultA = await service.ConvertAsync(inputPath, outputA);
                ConversionResult resultB = await service.ConvertAsync(inputPath, outputB);

                Dictionary<string, string> checksumsA = ComputeDirectoryChecksums(resultA.OutputDirectory);
                Dictionary<string, string> checksumsB = ComputeDirectoryChecksums(resultB.OutputDirectory);

                Assert.Equal(checksumsA.Keys.OrderBy(k => k).ToList(), checksumsB.Keys.OrderBy(k => k).ToList());
                foreach (string key in checksumsA.Keys)
                {
                    Assert.Equal(checksumsA[key], checksumsB[key]);
                }
            }
            finally
            {
                File.Delete(inputPath);
            }
        }
        finally
        {
            if (Directory.Exists(outputA))
            {
                Directory.Delete(outputA, recursive: true);
            }

            if (Directory.Exists(outputB))
            {
                Directory.Delete(outputB, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ConvertAsync_SyntheticSng_CompletesWithinSloWallTimeCeiling()
    {
        byte[] container = BuildMinimalSng("Perf Artist", "Perf Song", "perfcharter");
        string outputRoot = Path.Combine(Path.GetTempPath(), $"charthub-slo-{Guid.NewGuid():N}");

        try
        {
            string inputPath = await WriteTempSngAsync(container);

            try
            {
                Directory.CreateDirectory(outputRoot);

                var service = new ConversionService();
                var sw = Stopwatch.StartNew();
                await service.ConvertAsync(inputPath, outputRoot);
                sw.Stop();

                Assert.True(
                    sw.ElapsedMilliseconds < SloWallTimeCeilingMs,
                    $"Synthetic SNG conversion exceeded SLO ceiling of {SloWallTimeCeilingMs}ms. Actual: {sw.ElapsedMilliseconds}ms.");
            }
            finally
            {
                File.Delete(inputPath);
            }
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
    public async Task ConvertAsync_SyntheticSng_AllocatesUnderCeiling()
    {
        byte[] container = BuildMinimalSng("Alloc Artist", "Alloc Song", "alloccharter");
        string outputRoot = Path.Combine(Path.GetTempPath(), $"charthub-alloc-{Guid.NewGuid():N}");

        try
        {
            string inputPath = await WriteTempSngAsync(container);

            try
            {
                Directory.CreateDirectory(outputRoot);

                long before = GC.GetTotalAllocatedBytes(precise: false);
                var service = new ConversionService();
                await service.ConvertAsync(inputPath, outputRoot);
                long after = GC.GetTotalAllocatedBytes(precise: false);

                long allocated = after - before;
                Assert.True(
                    allocated < AllocationCeilingBytes,
                    $"Synthetic SNG conversion allocated {allocated:N0} bytes, exceeding ceiling of {AllocationCeilingBytes:N0} bytes.");
            }
            finally
            {
                File.Delete(inputPath);
            }
        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    private static Dictionary<string, string> ComputeDirectoryChecksums(string directory)
    {
        return Directory
            .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/'),
                path =>
                {
                    using var sha = SHA256.Create();
                    using FileStream stream = File.OpenRead(path);
                    return Convert.ToHexString(sha.ComputeHash(stream));
                },
                StringComparer.Ordinal);
    }

    private static async Task<string> WriteTempSngAsync(byte[] container)
    {
        string path = Path.Combine(Path.GetTempPath(), $"charthub-gate-{Guid.NewGuid():N}.sng");
        await File.WriteAllBytesAsync(path, container);
        return path;
    }

    private static byte[] BuildMinimalSng(string artist, string title, string charter)
    {
        byte[] songIni = System.Text.Encoding.UTF8.GetBytes(
            $"[song]\r\nartist = {artist}\r\ntitle = {title}\r\ncharter = {charter}\r\n");
        byte[] notesMid = BuildMinimalMidi();
        byte[] songOpus = [0x4F, 0x67, 0x67, 0x53]; // OggS magic

        return BuildSngContainer([
            ("song.ini", songIni),
            ("notes.mid", notesMid),
            ("song.opus", songOpus),
        ]);
    }

    private static byte[] BuildMinimalMidi()
    {
        // Minimal valid SMF: MThd (6 bytes) + empty MTrk (8 bytes)
        return [
            // MThd chunk
            0x4D, 0x54, 0x68, 0x64, // "MThd"
            0x00, 0x00, 0x00, 0x06, // length=6
            0x00, 0x00,             // format=0
            0x00, 0x01,             // numTracks=1
            0x00, 0x60,             // division=96 ticks/quarter
            // MTrk chunk
            0x4D, 0x54, 0x72, 0x6B, // "MTrk"
            0x00, 0x00, 0x00, 0x04, // length=4
            0x00, 0xFF, 0x2F, 0x00, // delta=0, end-of-track meta event
        ];
    }

    private static byte[] BuildSngContainer(IReadOnlyList<(string Name, byte[] Data)> files)
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
            tablePos = WriteSngEntry(bytes, tablePos, name, (ulong)currentOffset, (ulong)data.Length);
            Buffer.BlockCopy(data, 0, bytes, currentOffset, data.Length);
            currentOffset += data.Length;
        }

        return bytes;
    }

    private static int WriteSngEntry(byte[] bytes, int pos, string name, ulong offset, ulong length)
    {
        byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
        bytes[pos++] = (byte)nameBytes.Length;
        Array.Copy(nameBytes, 0, bytes, pos, nameBytes.Length);
        pos += nameBytes.Length;
        WriteSngUInt64LE(bytes, pos, offset); pos += 8;
        WriteSngUInt64LE(bytes, pos, length); pos += 8;
        return pos;
    }

    private static void WriteSngUInt64LE(byte[] bytes, int pos, ulong value)
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
/// M6 resilience gate: partial-failure and corrupt-input paths must throw deterministically.
/// </summary>
public sealed class StagingGateResilienceTests
{
    [Fact]
    public async Task ConvertAsync_CorruptSngBytes_ThrowsInvalidDataException()
    {
        string path = Path.Combine(Path.GetTempPath(), $"charthub-corrupt-{Guid.NewGuid():N}.sng");
        await File.WriteAllBytesAsync(path, [0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x00, 0x00, 0x00]);

        try
        {
            string outputRoot = Path.Combine(Path.GetTempPath(), $"charthub-corrupt-out-{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(outputRoot);
                var service = new ConversionService();
                await Assert.ThrowsAsync<InvalidDataException>(
                    () => service.ConvertAsync(path, outputRoot));
            }
            finally
            {
                if (Directory.Exists(outputRoot))
                {
                    Directory.Delete(outputRoot, recursive: true);
                }
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ConvertAsync_UnsupportedExtension_ThrowsNotSupportedException()
    {
        string path = Path.Combine(Path.GetTempPath(), $"charthub-unsupported-{Guid.NewGuid():N}.xyz");
        await File.WriteAllBytesAsync(path, [0x00]);

        try
        {
            string outputRoot = Path.Combine(Path.GetTempPath(), $"charthub-unsupported-out-{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(outputRoot);
                var service = new ConversionService();
                await Assert.ThrowsAsync<NotSupportedException>(
                    () => service.ConvertAsync(path, outputRoot));
            }
            finally
            {
                if (Directory.Exists(outputRoot))
                {
                    Directory.Delete(outputRoot, recursive: true);
                }
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ConvertAsync_SngWithNoChartFile_ThrowsInvalidDataException()
    {
        byte[] songIni = System.Text.Encoding.UTF8.GetBytes("[song]\r\nartist = Test\r\ntitle = Test\r\n");
        byte[] songOpus = [0x4F, 0x67, 0x67, 0x53];
        byte[] container = BuildSngContainer([
            ("song.ini", songIni),
            ("song.opus", songOpus),
        ]);

        string path = Path.Combine(Path.GetTempPath(), $"charthub-nochart-{Guid.NewGuid():N}.sng");
        await File.WriteAllBytesAsync(path, container);

        try
        {
            string outputRoot = Path.Combine(Path.GetTempPath(), $"charthub-nochart-out-{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(outputRoot);
                var service = new ConversionService();
                await Assert.ThrowsAsync<InvalidDataException>(
                    () => service.ConvertAsync(path, outputRoot));
            }
            finally
            {
                if (Directory.Exists(outputRoot))
                {
                    Directory.Delete(outputRoot, recursive: true);
                }
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] BuildSngContainer(IReadOnlyList<(string Name, byte[] Data)> files)
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
            tablePos = WriteSngEntry(bytes, tablePos, name, (ulong)currentOffset, (ulong)data.Length);
            Buffer.BlockCopy(data, 0, bytes, currentOffset, data.Length);
            currentOffset += data.Length;
        }

        return bytes;
    }

    private static int WriteSngEntry(byte[] bytes, int pos, string name, ulong offset, ulong length)
    {
        byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
        bytes[pos++] = (byte)nameBytes.Length;
        Array.Copy(nameBytes, 0, bytes, pos, nameBytes.Length);
        pos += nameBytes.Length;
        WriteSngUInt64LE(bytes, pos, offset); pos += 8;
        WriteSngUInt64LE(bytes, pos, length); pos += 8;
        return pos;
    }

    private static void WriteSngUInt64LE(byte[] bytes, int pos, ulong value)
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
