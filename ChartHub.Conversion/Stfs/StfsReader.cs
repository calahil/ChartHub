using System.Text;

namespace ChartHub.Conversion.Stfs;

/// <summary>An entry inside an STFS (CON) package — either a file or a directory.</summary>
internal sealed class StfsEntry
{
    public required string Name { get; init; }
    public bool IsDirectory { get; init; }
    public int FirstBlock { get; init; }
    public int FileSize { get; init; }

    /// <summary>
    /// When true, blocks are laid out sequentially (first, first+1, first+2, …).
    /// Corresponds to bit 6 (0x40) of the directory-entry flags byte.
    /// Fan-made tools (C3 CON Tools, Le Fluffie, RB3Maker) do not set this flag,
    /// but new Onyx packages and official DLC do.
    /// </summary>
    public bool IsConsecutive { get; init; }

    /// <summary>
    /// Index of the parent directory entry. -1 = root.
    /// STFS encodes parent as <c>parentIndex * 256</c>; we store the decoded index.
    /// </summary>
    public int ParentIndex { get; init; }
    public int EntryIndex { get; init; }
}

/// <summary>
/// Reads the file tree and extracts file content from an STFS "CON" package.
/// </summary>
/// <remarks>
/// <para>Supports the female (CON-signed) STFS topology used by Rock Band song packages.</para>
/// <para>
/// Block addressing formula (female topology, one level of hash tables):
/// <code>file_offset(N) = headerSize + (N + N/170 + 1) * 0x1000</code>
/// Level-0 hash table for group G:
/// <code>hash_offset(G) = headerSize + G * 171 * 0x1000</code>
/// Hash entry layout (24 bytes per logical block):
/// <list type="table">
///   <item><term>[0–19]</term><description>SHA-1 of block content</description></item>
///   <item><term>[20]</term><description>Status: 0x00 = sequential implicit next; non-zero = explicit pointer</description></item>
///   <item><term>[21–23]</term><description>Next block index (big-endian); 0xFFFFFF = last block</description></item>
/// </list>
/// </para>
/// <para>
/// Fan-made CON files (produced by C3 CON Tools, Le Fluffie, RB3Maker, etc.) often
/// have valid SHA-1 bytes but garbage status/next-block values in hash tables beyond
/// group 0 or 1. When a resolved next-block pointer would seek past the end of the
/// stream, <see cref="GetNextBlock"/> silently falls back to the sequential n+1 address,
/// matching the behaviour of Onyx for the <c>fe_Consecutive</c> fast path.
/// Files that set the consecutive flag (bit 6 of the directory-entry flags byte) skip
/// hash-table traversal entirely.
/// </para>
/// </remarks>
internal sealed class StfsReader : IDisposable
{
    private const int BlockSize = 0x1000;
    private const int HashEntriesPerGroup = 170;
    private const int HashEntrySize = 24;
    private const int DirectoryEntrySize = 64;
    private const int FlagIsDirectory = 0x80;
    private const int FlagNameLenMask = 0x3F;
    private const int LastBlock = 0xFFFFFF;

    private readonly Stream _stream;
    private readonly long _streamLength;
    private readonly int _headerSize;
    private readonly int _fileTableFirstBlock;
    private readonly int _fileTableBlockCount;
    private List<StfsEntry>? _entries;
    private bool _disposed;

    private StfsReader(Stream stream, long streamLength, int headerSize, int fileTableFirstBlock, int fileTableBlockCount)
    {
        _stream = stream;
        _streamLength = streamLength;
        _headerSize = headerSize;
        _fileTableFirstBlock = fileTableFirstBlock;
        _fileTableBlockCount = fileTableBlockCount;
    }

    /// <summary>Opens a CON file and parses the directory table.</summary>
    public static StfsReader Open(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            return Open(stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Opens a CON stream and parses the directory table.</summary>
    public static StfsReader Open(Stream stream)
    {
        byte[] header = new byte[0x400];
        ReadExact(stream, header, 0, header.Length);

        // Verify magic "CON "
        if (header[0] != 0x43 || header[1] != 0x4F || header[2] != 0x4E || header[3] != 0x20)
        {
            throw new InvalidDataException("Not a valid CON STFS package (missing 'CON ' magic).");
        }

        // Header size is stored at 0x340 as a 4-byte big-endian value, then rounded up to 0x1000.
        int rawHeaderSize = ReadBE32(header, 0x340);
        int headerSize = (rawHeaderSize + 0xFFF) & ~0xFFF;

        // STFS volume descriptor at 0x379 (CON packages use this offset).
        // Layout: [0] size, [1] reserved, [2] block_separation,
        //         [3–4] file_table_block_count (LE16),
        //         [5–7] file_table_block_num (LE24).
        int fileTableBlockCount = header[0x37C] | (header[0x37D] << 8);
        int fileTableFirstBlock = header[0x37E] | (header[0x37F] << 8) | (header[0x380] << 16);

        long streamLength = stream.Length;
        var reader = new StfsReader(stream, streamLength, headerSize, fileTableFirstBlock, fileTableBlockCount);
        reader.ParseEntries();
        return reader;
    }

    private void ParseEntries()
    {
        // Read all directory blocks into a contiguous buffer.
        byte[] dirData = ReadFile(_fileTableFirstBlock, _fileTableBlockCount * BlockSize);

        var entries = new List<StfsEntry>();
        int maxEntries = (_fileTableBlockCount * BlockSize) / DirectoryEntrySize;

        for (int i = 0; i < maxEntries; i++)
        {
            int offset = i * DirectoryEntrySize;
            byte flags = dirData[offset + 0x28];
            int nameLen = flags & FlagNameLenMask;
            if (nameLen == 0)
            {
                continue; // deleted / empty entry
            }

            string name = Encoding.ASCII.GetString(dirData, offset, nameLen);
            bool isDir = (flags & FlagIsDirectory) != 0;

            // Bit 6 (0x40) = consecutive: blocks are laid out sequentially (firstBlock, firstBlock+1, …).
            // Onyx sets this on files it creates; most fan tools do not, but blocks may still be
            // contiguous in practice — see GetNextBlock for the bounds-fallback that handles this.
            bool isConsecutive = (flags & 0x40) != 0;

            // first_block: bytes [0x2F–0x31], little-endian 24-bit
            int firstBlock = dirData[offset + 0x2F]
                | (dirData[offset + 0x30] << 8)
                | (dirData[offset + 0x31] << 16);

            // parent_dir_index: bytes [0x32–0x33], little-endian 16-bit, stored as index*256
            int parentRaw = dirData[offset + 0x32] | (dirData[offset + 0x33] << 8);
            int parentIndex = parentRaw == 0xFFFF ? -1 : parentRaw / 256;

            // file_size: bytes [0x34–0x37], big-endian 32-bit
            int fileSize = (dirData[offset + 0x34] << 24)
                | (dirData[offset + 0x35] << 16)
                | (dirData[offset + 0x36] << 8)
                | dirData[offset + 0x37];

            entries.Add(new StfsEntry
            {
                Name = name,
                IsDirectory = isDir,
                IsConsecutive = isConsecutive,
                FirstBlock = firstBlock,
                FileSize = fileSize,
                ParentIndex = parentIndex,
                EntryIndex = i,
            });
        }

        _entries = entries;
    }

    private List<StfsEntry> Entries
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _entries ?? throw new InvalidOperationException("Entries not yet parsed.");
        }
    }

    /// <summary>Returns all file entries in the package, with their virtual paths.</summary>
    public IReadOnlyList<(string VirtualPath, StfsEntry Entry)> GetAllFiles()
    {
        var result = new List<(string, StfsEntry)>();
        foreach (StfsEntry entry in Entries)
        {
            if (!entry.IsDirectory)
            {
                result.Add((BuildPath(entry), entry));
            }
        }

        return result;
    }

    /// <summary>Reads the content of a file entry into a byte array.</summary>
    public byte[] ReadEntry(StfsEntry entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return ReadFile(entry.FirstBlock, entry.FileSize, entry.IsConsecutive);
    }

    /// <summary>
    /// Reads the content of a file entry while overriding block traversal mode.
    /// </summary>
    /// <param name="entry">The STFS file entry to read.</param>
    /// <param name="forceConsecutive">
    /// When true, blocks are read as first, first+1, first+2 regardless of hash pointers.
    /// </param>
    public byte[] ReadEntry(StfsEntry entry, bool forceConsecutive)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return ReadFile(entry.FirstBlock, entry.FileSize, forceConsecutive);
    }

    /// <summary>Reads the content of a file entry by its virtual path (case-insensitive).</summary>
    public byte[]? ReadFile(string virtualPath)
    {
        foreach ((string path, StfsEntry entry) in GetAllFiles())
        {
            if (string.Equals(path, virtualPath, StringComparison.OrdinalIgnoreCase))
            {
                return ReadEntry(entry);
            }
        }

        return null;
    }

    /// <summary>Enumerates file entries under the given virtual directory prefix.</summary>
    public IEnumerable<(string VirtualPath, StfsEntry Entry)> EnumerateFolder(string folderPath)
    {
        string prefix = folderPath.TrimEnd('/') + "/";
        foreach ((string path, StfsEntry entry) in GetAllFiles())
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                yield return (path, entry);
            }
        }
    }

    private string BuildPath(StfsEntry entry)
    {
        List<StfsEntry> entries = Entries;
        var parts = new List<string> { entry.Name };
        int current = entry.ParentIndex;
        while (current >= 0 && current < entries.Count)
        {
            StfsEntry parent = entries[current];
            parts.Add(parent.Name);
            current = parent.ParentIndex;
        }

        parts.Reverse();
        return string.Join("/", parts);
    }

    /// <summary>
    /// Reads <paramref name="maxBytes"/> bytes starting from <paramref name="firstBlock"/>,
    /// following the block chain encoded in the level-0 hash tables.
    /// </summary>
    /// <param name="consecutive">
    /// When <see langword="true"/> (the directory-entry consecutive flag is set), blocks are
    /// known to be contiguous and hash-table traversal is skipped entirely.
    /// </param>
    private byte[] ReadFile(int firstBlock, int maxBytes, bool consecutive = false)
    {
        if (maxBytes <= 0)
        {
            return [];
        }

        int numBlocks = (maxBytes + BlockSize - 1) / BlockSize;
        byte[] result = new byte[maxBytes];
        int written = 0;
        int current = firstBlock;

        for (int i = 0; i < numBlocks && current != LastBlock; i++)
        {
            long fileOffset = BlockFileOffset(current);
            int bytesToRead = Math.Min(BlockSize, maxBytes - written);

            if (fileOffset < 0 || fileOffset + bytesToRead > _streamLength)
            {
                throw new InvalidDataException($"STFS block {current} resolves to invalid file offset {fileOffset} (stream length {_streamLength}).");
            }

            ReadAt(_stream, result, written, bytesToRead, fileOffset);
            written += bytesToRead;

            if (i < numBlocks - 1)
            {
                // Fast path: consecutive flag means blocks are n, n+1, n+2, …
                // (matches Onyx's fe_Consecutive optimisation).
                current = consecutive ? current + 1 : GetNextBlock(current);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns the next logical block in the chain for block N by reading the level-0 hash table.
    /// </summary>
    /// <remarks>
    /// Fan-made CON files (C3 CON Tools, Le Fluffie, RB3Maker, etc.) sometimes have valid SHA-1
    /// bytes but garbage status/next-block values in hash tables beyond group 0 or 1.  When the
    /// resolved next block would place us past the physical end of the stream we treat blocks as
    /// sequential (n+1), which is always correct for these contiguous fan packs.
    /// </remarks>
    private int GetNextBlock(int n)
    {
        int group = n / HashEntriesPerGroup;
        int idx = n % HashEntriesPerGroup;
        long hashOffset = _headerSize
            + ((long)group * (HashEntriesPerGroup + 1) * BlockSize)
            + ((long)idx * HashEntrySize);

        if (hashOffset < 0 || hashOffset + HashEntrySize > _streamLength)
        {
            return n + 1;
        }

        byte[] entry = new byte[HashEntrySize];
        ReadAt(_stream, entry, 0, HashEntrySize, hashOffset);

        byte status = entry[20];
        int next = (entry[21] << 16) | (entry[22] << 8) | entry[23];

        // Status 0x00 = sequential implicit: the next block is n+1.
        if (status == 0x00 && next == 0)
        {
            return n + 1;
        }

        // Validate that the next block's data offset is within the stream.
        // Fan-made tools can write garbage next-block pointers (e.g. multi-million block numbers)
        // in group 2+ hash tables while blocks are still physically contiguous.  Fall back to n+1
        // so we continue reading the actual contiguous data rather than seeking past EOF.
        if (next != LastBlock)
        {
            long nextDataOffset = BlockFileOffset(next);
            if (nextDataOffset < 0 || nextDataOffset + BlockSize > _streamLength)
            {
                return n + 1;
            }
        }

        return next; // 0xFFFFFF = last block
    }

    private long BlockFileOffset(int n)
    {
        return _headerSize + ((long)n + (n / HashEntriesPerGroup) + 1L) * BlockSize;
    }

    private static void ReadAt(Stream stream, byte[] buffer, int bufferOffset, int count, long fileOffset)
    {
        if (fileOffset < 0)
        {
            throw new InvalidDataException($"Attempted to seek to a negative offset ({fileOffset}).");
        }

        if (fileOffset > stream.Length)
        {
            throw new EndOfStreamException($"Attempted to seek to offset {fileOffset} beyond stream length {stream.Length}.");
        }

        stream.Seek(fileOffset, SeekOrigin.Begin);
        ReadExact(stream, buffer, bufferOffset, count);
    }

    private static void ReadExact(Stream stream, byte[] buffer, int offset, int count)
    {
        int remaining = count;
        while (remaining > 0)
        {
            int read = stream.Read(buffer, offset + (count - remaining), remaining);
            if (read == 0)
            {
                throw new EndOfStreamException($"Unexpected end of stream: needed {count} bytes, got {count - remaining}.");
            }

            remaining -= read;
        }
    }

    private static int ReadBE32(byte[] data, int offset)
    {
        return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _stream.Dispose();
        }
    }
}
