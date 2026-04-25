using System.Text;

namespace ChartHub.Conversion.Midi;

/// <summary>
/// Generates expert+.mid from notes.mid by merging 2x kick pedal (note 95) into
/// the Expert kick (note 96) on the PART DRUMS track, then removing note 95.
/// </summary>
/// <remarks>
/// Mirrors Onyx <c>Drums.expertWith2x</c>: drumKick2x events (note 95) are merged
/// into Expert drumGems as Kick (note 96) and note 95 is cleared from the track.
/// If no PART DRUMS track is present, the output is byte-identical to the input.
/// </remarks>
internal static class ExpertPlusMidiGenerator
{
    private const byte Kick2xNote = 95;
    private const byte KickNote = 96;
    private const string PartDrumsTrackName = "PART DRUMS";

    /// <summary>
    /// Applies the expertWith2x transform to <paramref name="notesMidiBytes"/> and returns
    /// the resulting expert+.mid byte array.
    /// </summary>
    public static byte[] Apply(byte[] notesMidiBytes)
    {
        if (notesMidiBytes.Length < 14)
        {
            return notesMidiBytes;
        }

        int pos = 0;
        byte[] fileHeader = ReadExact(notesMidiBytes, ref pos, 14);

        if (fileHeader[0] != 'M' || fileHeader[1] != 'T' || fileHeader[2] != 'h' || fileHeader[3] != 'd')
        {
            return notesMidiBytes;
        }

        int format = ReadBE16(fileHeader, 8);
        int trackCount = ReadBE16(fileHeader, 10);
        int division = ReadBE16(fileHeader, 12);

        var tracks = new List<byte[]>(trackCount);
        for (int t = 0; t < trackCount; t++)
        {
            if (pos + 8 > notesMidiBytes.Length)
            {
                break;
            }

            byte[] trackHeader = ReadExact(notesMidiBytes, ref pos, 8);
            if (trackHeader[0] != 'M' || trackHeader[1] != 'T' || trackHeader[2] != 'r' || trackHeader[3] != 'k')
            {
                break;
            }

            int trackLen = ReadBE32(trackHeader, 4);
            if (trackLen < 0 || pos + trackLen > notesMidiBytes.Length)
            {
                break;
            }

            tracks.Add(ReadExact(notesMidiBytes, ref pos, trackLen));
        }

        // Apply expertWith2x to the PART DRUMS track; keep all others unchanged.
        var outputTracks = new List<byte[]>(tracks.Count);
        foreach (byte[] track in tracks)
        {
            string? name = GetTrackName(track);
            outputTracks.Add(
                string.Equals(name, PartDrumsTrackName, StringComparison.OrdinalIgnoreCase)
                    ? TransformDrumsTrack(track)
                    : track);
        }

        using var ms = new MemoryStream(notesMidiBytes.Length);
        ms.Write("MThd"u8);
        WriteUInt32BE(ms, 6);
        WriteUInt16BE(ms, (ushort)format);
        WriteUInt16BE(ms, (ushort)outputTracks.Count);
        WriteUInt16BE(ms, (ushort)division);

        foreach (byte[] track in outputTracks)
        {
            ms.Write("MTrk"u8);
            WriteUInt32BE(ms, (uint)track.Length);
            ms.Write(track);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Walks the PART DRUMS track event stream and remaps note 95 (2x kick) to note 96 (kick).
    /// Running status is preserved; only the note data byte is altered.
    /// </summary>
    internal static byte[] TransformDrumsTrack(byte[] trackData)
    {
        using var output = new MemoryStream(trackData.Length);
        int pos = 0;
        byte runningStatus = 0;

        while (pos < trackData.Length)
        {
            // Delta-time: copy variable-length bytes verbatim.
            CopyVarLen(trackData, ref pos, output);
            if (pos >= trackData.Length)
            {
                break;
            }

            byte b = trackData[pos];

            // Meta event (FF)
            if (b == 0xFF)
            {
                pos++;
                output.WriteByte(0xFF);
                runningStatus = 0;

                if (pos >= trackData.Length)
                {
                    break;
                }

                byte metaType = trackData[pos++];
                output.WriteByte(metaType);
                CopyVarLenAndData(trackData, ref pos, output);
                continue;
            }

            // SysEx
            if (b == 0xF0 || b == 0xF7)
            {
                pos++;
                output.WriteByte(b);
                runningStatus = 0;
                CopyVarLenAndData(trackData, ref pos, output);
                continue;
            }

            // New status byte
            if (b >= 0x80)
            {
                runningStatus = b;
                pos++;
                output.WriteByte(b);
            }
            // else: running status — current byte is first data byte; no status written.

            byte statusType = (byte)(runningStatus & 0xF0);

            if (statusType == 0x90 || statusType == 0x80)
            {
                // NoteOn / NoteOff: two data bytes [note] [velocity].
                // NoteOn with velocity 0 is a silent NoteOff — still remap note 95.
                if (pos + 1 >= trackData.Length)
                {
                    break;
                }

                byte note = trackData[pos++];
                byte vel = trackData[pos++];
                output.WriteByte(note == Kick2xNote ? KickNote : note);
                output.WriteByte(vel);
            }
            else if (statusType == 0xA0 || statusType == 0xB0 || statusType == 0xE0)
            {
                // Aftertouch / Control Change / Pitch Bend: two data bytes.
                if (pos + 1 >= trackData.Length)
                {
                    break;
                }

                output.WriteByte(trackData[pos++]);
                output.WriteByte(trackData[pos++]);
            }
            else if (statusType == 0xC0 || statusType == 0xD0)
            {
                // Program Change / Channel Pressure: one data byte.
                if (pos >= trackData.Length)
                {
                    break;
                }

                output.WriteByte(trackData[pos++]);
            }
            else
            {
                // Unknown status byte — stop processing to avoid corrupting the output.
                break;
            }
        }

        return output.ToArray();
    }

    // ---------- private utilities ----------

    private static string? GetTrackName(byte[] trackData)
    {
        int pos = 0;
        while (pos < trackData.Length)
        {
            SkipVarLen(trackData, ref pos);
            if (pos >= trackData.Length)
            {
                break;
            }

            byte status = trackData[pos++];

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

                if (metaType == 0x03)
                {
                    return Encoding.ASCII.GetString(trackData, pos, metaLen);
                }

                if (metaType == 0x2F)
                {
                    break;
                }

                pos += metaLen;
                continue;
            }

            // Skip non-meta event data bytes.
            byte type = (byte)(status & 0xF0);
            if (type == 0x80 || type == 0x90 || type == 0xA0 || type == 0xB0 || type == 0xE0)
            {
                pos += 2;
            }
            else if (type == 0xC0 || type == 0xD0)
            {
                pos += 1;
            }
            else
            {
                break;
            }
        }

        return null;
    }

    /// <summary>Copies variable-length quantity bytes from <paramref name="data"/> to <paramref name="output"/>.</summary>
    private static void CopyVarLen(byte[] data, ref int pos, MemoryStream output)
    {
        while (pos < data.Length)
        {
            byte b = data[pos++];
            output.WriteByte(b);
            if ((b & 0x80) == 0)
            {
                break;
            }
        }
    }

    /// <summary>Reads a variable-length count then copies that many data bytes to <paramref name="output"/>.</summary>
    private static void CopyVarLenAndData(byte[] data, ref int pos, MemoryStream output)
    {
        int len = 0;
        while (pos < data.Length)
        {
            byte b = data[pos++];
            output.WriteByte(b);
            len = (len << 7) | (b & 0x7F);
            if ((b & 0x80) == 0)
            {
                break;
            }
        }

        if (len > 0 && pos + len <= data.Length)
        {
            output.Write(data, pos, len);
            pos += len;
        }
    }

    private static void SkipVarLen(byte[] data, ref int pos)
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

    private static byte[] ReadExact(byte[] source, ref int pos, int count)
    {
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
