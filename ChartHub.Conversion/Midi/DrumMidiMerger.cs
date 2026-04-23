using System.Text;

namespace ChartHub.Conversion.Midi;

/// <summary>
/// Merges a General MIDI drum track (channel 10) to a Clone Hero <c>PART DRUMS</c> Expert
/// track and merges it into an existing Clone Hero / Rock Band MIDI file.
/// </summary>
public interface IDrumMidiMerger
{
    /// <summary>
    /// Returns a new MIDI byte array that contains all tracks from
    /// <paramref name="existingMidi"/> plus a new <c>PART DRUMS</c> track
    /// derived by mapping GM channel-10 notes in <paramref name="gmDrumMidi"/>
    /// to the CH Expert drum note layout (base 96).
    /// If <paramref name="existingMidi"/> already has a <c>PART DRUMS</c> track it is
    /// left unchanged and the original bytes are returned.
    /// </summary>
    byte[] MergeGeneratedDrums(byte[] existingMidi, byte[] gmDrumMidi);
}

/// <summary>
/// Converts a General MIDI drum track (channel 10) to a Clone Hero <c>PART DRUMS</c> Expert
/// track and merges it into an existing Clone Hero / Rock Band MIDI file.
/// </summary>
/// <remarks>
/// GM → CH mapping (from onyx Drums.hs, MIT):
/// <list type="bullet">
///   <item>35, 36  → 96 Kick</item>
///   <item>37–40   → 97 Red (Snare)</item>
///   <item>42, 44, 46 → 98 Yellow (Hi-hat)</item>
///   <item>47, 48, 50 → 99 Blue (High/Mid Tom)</item>
///   <item>41, 43, 45 → 100 Green (Low/Floor Tom)</item>
///   <item>49, 52, 55, 57 → 100 Green (Crash → Green)</item>
///   <item>51, 53, 56, 59 → 101 Orange (Ride / Cowbell → 5th lane)</item>
/// </list>
/// </remarks>
public sealed class DrumMidiMerger : IDrumMidiMerger
{
    // GM note → CH Expert PART DRUMS note (Expert base = 96)
    private static readonly Dictionary<byte, byte> GmToChNote = new()
    {
        // Kick
        [35] = 96,
        [36] = 96,
        // Snare / Rim / Clap
        [37] = 97,
        [38] = 97,
        [39] = 97,
        [40] = 97,
        // Hi-hat (closed / pedal / open)
        [42] = 98,
        [44] = 98,
        [46] = 98,
        // High / Mid toms → Blue
        [47] = 99,
        [48] = 99,
        [50] = 99,
        // Low / Floor toms + Crash → Green
        [41] = 100,
        [43] = 100,
        [45] = 100,
        [49] = 100,
        [52] = 100,
        [55] = 100,
        [57] = 100,
        // Ride / Cowbell → Orange (5th lane cymbal)
        [51] = 101,
        [53] = 101,
        [56] = 101,
        [59] = 101,
    };

    private const string PartDrumsTrackName = "PART DRUMS";

    public byte[] MergeGeneratedDrums(byte[] existingMidi, byte[] gmDrumMidi)
    {
        ArgumentNullException.ThrowIfNull(existingMidi);
        ArgumentNullException.ThrowIfNull(gmDrumMidi);

        // Parse existing MIDI header.
        int pos = 0;
        ValidateMThd(existingMidi, ref pos);
        int existingFormat = ReadBE16(existingMidi, 8);
        int existingTrackCount = ReadBE16(existingMidi, 10);
        int existingDivision = ReadBE16(existingMidi, 12);

        // Read all raw existing tracks.
        List<byte[]> existingTracks = ReadAllRawTracks(existingMidi, ref pos, existingTrackCount);

        // Check if PART DRUMS already exists — if so, return original unchanged.
        foreach (byte[] trackData in existingTracks)
        {
            string? name = GetTrackName(trackData);
            if (string.Equals(name, PartDrumsTrackName, StringComparison.OrdinalIgnoreCase))
            {
                return existingMidi;
            }
        }

        // Parse GM MIDI to get its division.
        int gmPos = 0;
        ValidateMThd(gmDrumMidi, ref gmPos);
        int gmDivision = ReadBE16(gmDrumMidi, 12);
        int gmTrackCount = ReadBE16(gmDrumMidi, 10);

        List<byte[]> gmTracks = ReadAllRawTracks(gmDrumMidi, ref gmPos, gmTrackCount);

        // Collect all channel-10 notes (absolute ticks) across all GM tracks.
        List<(long AbsTick, bool IsOn, byte ChNote, byte Velocity)> noteEvents = [];
        foreach (byte[] gmTrack in gmTracks)
        {
            CollectCh10Notes(gmTrack, gmDivision, existingDivision, noteEvents);
        }

        if (noteEvents.Count == 0)
        {
            return existingMidi;
        }

        // Sort by absolute tick, then build the raw PART DRUMS track.
        noteEvents.Sort((a, b) => a.AbsTick.CompareTo(b.AbsTick));
        byte[] drumTrackData = BuildPartDrumsTrack(noteEvents);

        // Emit new MIDI with all existing tracks + new PART DRUMS track.
        int newTrackCount = existingTracks.Count + 1;
        using MemoryStream ms = new();

        ms.Write("MThd"u8);
        WriteUInt32BE(ms, 6);
        WriteUInt16BE(ms, (ushort)existingFormat);
        WriteUInt16BE(ms, (ushort)newTrackCount);
        WriteUInt16BE(ms, (ushort)existingDivision);

        foreach (byte[] t in existingTracks)
        {
            ms.Write("MTrk"u8);
            WriteUInt32BE(ms, (uint)t.Length);
            ms.Write(t);
        }

        ms.Write("MTrk"u8);
        WriteUInt32BE(ms, (uint)drumTrackData.Length);
        ms.Write(drumTrackData);

        return ms.ToArray();
    }

    // ---------- build PART DRUMS raw MTrk chunk ----------

    private static byte[] BuildPartDrumsTrack(List<(long AbsTick, bool IsOn, byte ChNote, byte Velocity)> noteEvents)
    {
        using MemoryStream ms = new();

        // 1. Track name meta event: FF 03 len "PART DRUMS"
        byte[] trackNameBytes = Encoding.ASCII.GetBytes(PartDrumsTrackName);
        ms.WriteByte(0x00); // delta = 0
        ms.WriteByte(0xFF);
        ms.WriteByte(0x03);
        WriteVarLen(ms, trackNameBytes.Length);
        ms.Write(trackNameBytes);

        // 2. Note events (delta-ticked).
        long prevTick = 0;
        foreach ((long absTick, bool isOn, byte chNote, byte velocity) in noteEvents)
        {
            long delta = absTick - prevTick;
            prevTick = absTick;

            WriteVarLen(ms, (int)delta);
            byte statusByte = isOn ? (byte)0x90 : (byte)0x80; // channel 0
            ms.WriteByte(statusByte);
            ms.WriteByte(chNote);
            ms.WriteByte(isOn ? velocity : (byte)0x00);
        }

        // 3. End of track: FF 2F 00
        ms.WriteByte(0x00);
        ms.WriteByte(0xFF);
        ms.WriteByte(0x2F);
        ms.WriteByte(0x00);

        return ms.ToArray();
    }

    // ---------- GM parsing ----------

    private static void CollectCh10Notes(
        byte[] trackData,
        int srcDivision,
        int dstDivision,
        List<(long, bool, byte, byte)> output)
    {
        int pos = 0;
        long absTick = 0;
        byte runningStatus = 0;

        while (pos < trackData.Length)
        {
            long delta = ReadVarLenLong(trackData, ref pos);
            absTick += delta;

            if (pos >= trackData.Length)
            {
                break;
            }

            byte b = trackData[pos];

            if (b == 0xFF) // meta event
            {
                pos++;
                if (pos >= trackData.Length) { break; }
                byte metaType = trackData[pos++];
                int metaLen = ReadVarLenInt(trackData, ref pos);
                pos += metaLen;
                if (metaType == 0x2F) { break; } // end of track
                continue;
            }

            if (b == 0xF0 || b == 0xF7) // sysex
            {
                pos++;
                int sysexLen = ReadVarLenInt(trackData, ref pos);
                pos += sysexLen;
                continue;
            }

            byte status;
            if ((b & 0x80) != 0)
            {
                status = b;
                runningStatus = b;
                pos++;
            }
            else
            {
                status = runningStatus;
            }

            byte type = (byte)(status & 0xF0);
            byte channel = (byte)(status & 0x0F);

            if (type == 0x90 || type == 0x80)
            {
                if (pos + 1 >= trackData.Length) { break; }
                byte noteNumber = trackData[pos++];
                byte velocity = trackData[pos++];

                // Channel 10 in GM = index 9 (0-based).
                if (channel == 9 && GmToChNote.TryGetValue(noteNumber, out byte chNote))
                {
                    bool isNoteOn = type == 0x90 && velocity > 0;
                    long scaledTick = srcDivision == dstDivision
                        ? absTick
                        : (long)Math.Round((double)absTick * dstDivision / srcDivision);
                    output.Add((scaledTick, isNoteOn, chNote, velocity));
                }
            }
            else if (type == 0xA0 || type == 0xB0 || type == 0xE0)
            {
                pos += 2;
            }
            else if (type == 0xC0 || type == 0xD0)
            {
                pos += 1;
            }
            else
            {
                // Unknown — stop parsing this track.
                break;
            }
        }
    }

    // ---------- parsing helpers ----------

    private static void ValidateMThd(byte[] midi, ref int pos)
    {
        if (midi.Length < 14
            || midi[0] != 'M' || midi[1] != 'T' || midi[2] != 'h' || midi[3] != 'd')
        {
            throw new InvalidDataException("Not a valid Standard MIDI File (missing MThd).");
        }

        pos = 14;
    }

    private static List<byte[]> ReadAllRawTracks(byte[] midi, ref int pos, int trackCount)
    {
        List<byte[]> tracks = new(trackCount);
        for (int i = 0; i < trackCount; i++)
        {
            if (pos + 8 > midi.Length)
            {
                break;
            }

            if (midi[pos] != 'M' || midi[pos + 1] != 'T' || midi[pos + 2] != 'r' || midi[pos + 3] != 'k')
            {
                throw new InvalidDataException($"Expected MTrk at offset {pos}.");
            }

            int len = ReadBE32(midi, pos + 4);
            pos += 8;

            if (pos + len > midi.Length)
            {
                throw new InvalidDataException("Track length exceeds file bounds.");
            }

            tracks.Add(midi[pos..(pos + len)]);
            pos += len;
        }

        return tracks;
    }

    private static string? GetTrackName(byte[] trackData)
    {
        int pos = 0;
        while (pos < trackData.Length)
        {
            ReadVarLenLong(trackData, ref pos);

            if (pos >= trackData.Length) { break; }
            byte b = trackData[pos++];

            if (b == 0xFF)
            {
                if (pos >= trackData.Length) { break; }
                byte metaType = trackData[pos++];
                int metaLen = ReadVarLenInt(trackData, ref pos);
                if (pos + metaLen > trackData.Length) { break; }

                if (metaType == 0x03)
                {
                    return Encoding.ASCII.GetString(trackData, pos, metaLen);
                }

                if (metaType == 0x2F) { break; }
                pos += metaLen;
                continue;
            }

            // Skip any non-meta event.
            byte type = (byte)(b & 0xF0);
            if (type == 0x90 || type == 0x80 || type == 0xA0 || type == 0xB0 || type == 0xE0)
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

    private static long ReadVarLenLong(byte[] data, ref int pos)
    {
        long value = 0;
        while (pos < data.Length)
        {
            byte b = data[pos++];
            value = (value << 7) | (long)(b & 0x7F);
            if ((b & 0x80) == 0) { break; }
        }

        return value;
    }

    private static int ReadVarLenInt(byte[] data, ref int pos)
    {
        int value = 0;
        while (pos < data.Length)
        {
            byte b = data[pos++];
            value = (value << 7) | (int)(b & 0x7F);
            if ((b & 0x80) == 0) { break; }
        }

        return value;
    }

    private static void WriteVarLen(Stream s, int value)
    {
        if (value < 0x80)
        {
            s.WriteByte((byte)value);
            return;
        }

        Span<byte> buf = stackalloc byte[4];
        int count = 0;
        while (value > 0)
        {
            buf[count++] = (byte)(value & 0x7F);
            value >>= 7;
        }

        for (int i = count - 1; i >= 0; i--)
        {
            s.WriteByte(i > 0 ? (byte)(buf[i] | 0x80) : buf[i]);
        }
    }

    private static int ReadBE16(byte[] data, int offset)
        => (data[offset] << 8) | data[offset + 1];

    private static int ReadBE32(byte[] data, int offset)
        => (int)(((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3]);

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
