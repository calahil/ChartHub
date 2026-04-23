using System.Text;

namespace ChartHub.Conversion.Sng;

internal sealed record SngFileEntry(
    string Name,
    long Offset,
    long Length);

internal sealed record SngPackage(
    int Version,
    IReadOnlyList<SngFileEntry> Files);

/// <summary>
/// Reads fan-made SNGPKG containers and extracts their file table.
/// </summary>
internal static class SngPackageReader
{
    private static readonly byte[] SngMagic = Encoding.ASCII.GetBytes("SNGPKG");

    internal static bool TryFindEntry(SngPackage package, string fileName, out SngFileEntry? entry)
    {
        entry = package.Files.FirstOrDefault(f =>
            string.Equals(Path.GetFileName(f.Name), fileName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(f.Name, fileName, StringComparison.OrdinalIgnoreCase));
        return entry != null;
    }

    internal static byte[] ReadFileData(byte[] containerBytes, SngFileEntry entry)
    {
        if (entry.Offset < 0 || entry.Length <= 0)
        {
            throw new InvalidDataException($"Invalid SNG entry bounds for '{entry.Name}'.");
        }

        long end = entry.Offset + entry.Length;
        if (end < entry.Offset || end > containerBytes.Length)
        {
            throw new InvalidDataException($"SNG entry '{entry.Name}' exceeds container bounds.");
        }

        byte[] data = new byte[entry.Length];
        Buffer.BlockCopy(containerBytes, (int)entry.Offset, data, 0, (int)entry.Length);
        return data;
    }

    internal static SngPackage Read(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("SNG package file not found.", path);
        }

        byte[] bytes = File.ReadAllBytes(path);
        return Read(bytes);
    }

    internal static SngPackage Read(byte[] bytes)
    {
        if (bytes.Length < 8 || !bytes.AsSpan(0, SngMagic.Length).SequenceEqual(SngMagic))
        {
            throw new InvalidDataException("Not a SNGPKG container (missing SNGPKG magic).");
        }

        int version = bytes[6];

        List<SngFileEntry> entries = FindFileTable(bytes);
        if (entries.Count == 0)
        {
            throw new InvalidDataException("SNGPKG file table could not be parsed.");
        }

        return new SngPackage(version, entries);
    }

    private static List<SngFileEntry> FindFileTable(byte[] bytes)
    {
        // The observed SNGPKG format stores file entries as:
        // [nameLen:u8][name:ASCII][offset:u64 LE][length:u64 LE]
        // We locate the table by scanning for a run of at least 2 valid entries.
        for (int start = SngMagic.Length; start <= bytes.Length - 18; start++)
        {
            List<SngFileEntry> parsed = ParseEntriesAt(bytes, start);
            if (parsed.Count >= 2)
            {
                return parsed;
            }
        }

        return [];
    }

    private static List<SngFileEntry> ParseEntriesAt(byte[] bytes, int start)
    {
        var entries = new List<SngFileEntry>();
        int pos = start;

        while (pos <= bytes.Length - 18)
        {
            int nameLen = bytes[pos];
            if (nameLen <= 0 || nameLen > 127)
            {
                break;
            }

            int nameStart = pos + 1;
            int metaStart = nameStart + nameLen;
            if (metaStart + 16 > bytes.Length)
            {
                break;
            }

            ReadOnlySpan<byte> nameBytes = bytes.AsSpan(nameStart, nameLen);
            if (!IsReasonableName(nameBytes))
            {
                break;
            }

            string name = Encoding.ASCII.GetString(nameBytes);
            ulong offset = ReadUInt64LE(bytes, metaStart);
            ulong length = ReadUInt64LE(bytes, metaStart + 8);

            if (!IsBoundsValid(bytes.Length, offset, length))
            {
                break;
            }

            entries.Add(new SngFileEntry(name, (long)offset, (long)length));
            pos = metaStart + 16;
        }

        return entries;
    }

    private static ulong ReadUInt64LE(byte[] bytes, int offset)
    {
        return (ulong)bytes[offset]
            | ((ulong)bytes[offset + 1] << 8)
            | ((ulong)bytes[offset + 2] << 16)
            | ((ulong)bytes[offset + 3] << 24)
            | ((ulong)bytes[offset + 4] << 32)
            | ((ulong)bytes[offset + 5] << 40)
            | ((ulong)bytes[offset + 6] << 48)
            | ((ulong)bytes[offset + 7] << 56);
    }

    private static bool IsReasonableName(ReadOnlySpan<byte> nameBytes)
    {
        bool hasDot = false;
        for (int i = 0; i < nameBytes.Length; i++)
        {
            byte c = nameBytes[i];
            if (c == (byte)'.')
            {
                hasDot = true;
            }

            if (c < 0x20 || c > 0x7E)
            {
                return false;
            }
        }

        return hasDot;
    }

    private static bool IsBoundsValid(int fileLength, ulong offset, ulong length)
    {
        if (length == 0)
        {
            return false;
        }

        if (offset >= (ulong)fileLength)
        {
            return false;
        }

        ulong end = offset + length;
        if (end < offset)
        {
            return false;
        }

        return end <= (ulong)fileLength;
    }
}
