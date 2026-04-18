using System.Text;

namespace ChartHub.Conversion.Midi;

/// <summary>
/// Reads a Rock Band 3 Standard MIDI File (Format 1) and produces a Clone Hero–compatible
/// Standard MIDI File by removing RB-specific tracks.
/// </summary>
/// <remarks>
/// Tracks stripped for CH output: VENUE, BEAT, HARM2, HARM3, PART REAL_GUITAR,
/// PART REAL_BASS, PART REAL_KEYS (all difficulties), KEYS_ANIM_LH, KEYS_ANIM_RH.
/// The tempo track (track 0) is always preserved.
/// </remarks>
internal static class RbMidiConverter
{
    private static readonly HashSet<string> RbOnlyTrackNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "VENUE",
        "BEAT",
        "HARM2",
        "HARM3",
        "PART REAL_GUITAR",
        "PART REAL_GUITAR_22",
        "PART REAL_BASS",
        "PART REAL_BASS_22",
        "PART REAL_KEYS_E",
        "PART REAL_KEYS_M",
        "PART REAL_KEYS_H",
        "PART REAL_KEYS_X",
        "KEYS_ANIM_LH",
        "KEYS_ANIM_RH",
    };

    /// <summary>
    /// Converts RB3 MIDI bytes to CH-compatible MIDI bytes.
    /// Returns the filtered MIDI data.
    /// </summary>
    public static byte[] Convert(byte[] midiBytes)
    {
        int pos = 0;
        byte[] header = ReadExact(midiBytes, ref pos, 14);

        // Validate MThd header
        if (header[0] != 'M' || header[1] != 'T' || header[2] != 'h' || header[3] != 'd')
        {
            throw new InvalidDataException("Not a valid Standard MIDI File (missing MThd).");
        }

        int format = ReadBE16(header, 8);
        int trackCount = ReadBE16(header, 10);
        int division = ReadBE16(header, 12);

        if (format != 1)
        {
            // Format 0/2 — return as-is; CH can handle them
            return midiBytes;
        }

        // Read all tracks
        var tracks = new List<byte[]>(trackCount);
        for (int t = 0; t < trackCount; t++)
        {
            byte[] trackHeader = ReadExact(midiBytes, ref pos, 8);
            if (trackHeader[0] != 'M' || trackHeader[1] != 'T' || trackHeader[2] != 'r' || trackHeader[3] != 'k')
            {
                throw new InvalidDataException($"Expected MTrk at offset {pos - 8}.");
            }

            int trackLen = ReadBE32(trackHeader, 4);
            byte[] trackData = ReadExact(midiBytes, ref pos, trackLen);
            tracks.Add(trackData);
        }

        // Filter out RB-only tracks (keep track 0 always — it's the tempo map)
        var kept = new List<(int Index, byte[] Data)> { (0, tracks[0]) };
        for (int t = 1; t < tracks.Count; t++)
        {
            string? name = GetTrackName(tracks[t]);
            if (name == null || !RbOnlyTrackNames.Contains(name))
            {
                kept.Add((t, tracks[t]));
            }
        }

        // Write output MIDI
        using var ms = new MemoryStream();

        // MThd
        ms.Write("MThd"u8);
        WriteUInt32BE(ms, 6);
        WriteUInt16BE(ms, (ushort)format);
        WriteUInt16BE(ms, (ushort)kept.Count);
        WriteUInt16BE(ms, (ushort)division);

        foreach ((_, byte[] data) in kept)
        {
            ms.Write("MTrk"u8);
            WriteUInt32BE(ms, (uint)data.Length);
            ms.Write(data);
        }

        return ms.ToArray();
    }

    /// <summary>Extracts the track name from the first meta-event (FF 03) in the track data.</summary>
    private static string? GetTrackName(byte[] trackData)
    {
        int pos = 0;
        while (pos < trackData.Length)
        {
            // Read delta time (variable-length)
            ReadVarLen(trackData, ref pos);
            if (pos >= trackData.Length)
            {
                break;
            }

            byte status = trackData[pos++];

            // Meta event
            if (status == 0xFF)
            {
                if (pos >= trackData.Length)
                {
                    break;
                }

                byte metaType = trackData[pos++];
                int metaLen = ReadVarLenInt(trackData, ref pos);
                if (pos + metaLen > trackData.Length)
                {
                    break;
                }

                if (metaType == 0x03) // Track Name
                {
                    return Encoding.ASCII.GetString(trackData, pos, metaLen);
                }

                if (metaType == 0x2F)
                {
                    break; // End of track
                }

                pos += metaLen;
                continue;
            }

            // Skip non-meta events by determining event length
            int dataLen = GetMidiEventDataLength(status, trackData, pos);
            if (dataLen < 0)
            {
                break;
            }

            pos += dataLen;
        }

        return null;
    }

    private static int GetMidiEventDataLength(byte status, byte[] data, int pos)
    {
        byte type = (byte)(status & 0xF0);
        if (type == 0x80 || type == 0x90 || type == 0xA0 || type == 0xB0 || type == 0xE0)
        {
            return 2;
        }

        if (type == 0xC0 || type == 0xD0)
        {
            return 1;
        }

        if (status == 0xF0) // SysEx
        {
            int sysexLen = ReadVarLenIntPeek(data, pos);
            int headerBytes = VarLenByteCount(sysexLen);
            return sysexLen + headerBytes;
        }

        return -1;
    }

    private static void ReadVarLen(byte[] data, ref int pos)
    {
        while (pos < data.Length && (data[pos] & 0x80) != 0)
        {
            pos++;
        }

        if (pos < data.Length)
        {
            pos++;
        }
    }

    private static int ReadVarLenInt(byte[] data, ref int pos)
    {
        int value = 0;
        while (pos < data.Length)
        {
            byte b = data[pos++];
            value = (value << 7) | (b & 0x7F);
            if ((b & 0x80) == 0)
            {
                break;
            }
        }

        return value;
    }

    private static int ReadVarLenIntPeek(byte[] data, int pos)
    {
        int value = 0;
        while (pos < data.Length)
        {
            byte b = data[pos++];
            value = (value << 7) | (b & 0x7F);
            if ((b & 0x80) == 0)
            {
                break;
            }
        }

        return value;
    }

    private static int VarLenByteCount(int value)
    {
        if (value < 0x80)
        {
            return 1;
        }

        if (value < 0x4000)
        {
            return 2;
        }

        if (value < 0x200000)
        {
            return 3;
        }

        return 4;
    }

    private static byte[] ReadExact(byte[] source, ref int pos, int count)
    {
        if (pos + count > source.Length)
        {
            throw new EndOfStreamException($"Unexpected end of MIDI data at offset {pos}, needed {count} bytes.");
        }

        byte[] result = source[pos..(pos + count)];
        pos += count;
        return result;
    }

    private static int ReadBE16(byte[] data, int offset)
        => (data[offset] << 8) | data[offset + 1];

    private static int ReadBE32(byte[] data, int offset)
        => (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

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
}
