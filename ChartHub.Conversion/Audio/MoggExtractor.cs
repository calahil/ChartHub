using System.Security.Cryptography;

using ChartHub.Conversion.Dta;

using NVorbis;

using OggVorbisEncoder;

using StbVorbisSharp;

namespace ChartHub.Conversion.Audio;

/// <summary>
/// Extracts OGG Vorbis audio from a MOGG file.
/// </summary>
/// <remarks>
/// A MOGG file is an 8-byte header (<c>0A 00 00 00 &lt;offset-LE32&gt;</c>) followed by a
/// standard OGG Vorbis multistream. Audio extraction is fully internal and deterministic.
/// </remarks>
internal sealed class MoggExtractor
{
    public MoggExtractor() { }

    /// <summary>
    /// Returns the decoded duration of the backing OGG payload in seconds, or 0 when duration
    /// cannot be determined.
    /// </summary>
    public double EstimateBackingDurationSeconds(byte[] moggBytes)
    {
        byte[] rawOggBytes = ExtractRawOggBytes(moggBytes);
        try
        {
            using var stream = new MemoryStream(rawOggBytes, writable: false);
            using var reader = new VorbisReader(stream, closeOnDispose: false);
            return Math.Max(0, reader.TotalTime.TotalSeconds);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Extracts backing audio into <paramref name="outputDir"/>.
    /// For RB3CON/SNG files, this extracts the MOGG as a single backing track.
    /// </summary>
    /// <param name="moggBytes">Raw MOGG file bytes.</param>
    /// <param name="songInfo">DTA metadata (used for validation only).</param>
    /// <param name="outputDir">Directory where the backing audio will be written.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Map containing the backing track key "song" → file path.</returns>
    public async Task<IReadOnlyDictionary<string, string>> ExtractStemsAsync(
        byte[] moggBytes,
        DtaSongInfo songInfo,
        string outputDir,
        CancellationToken cancellationToken = default)
    {
        byte[] rawOggBytes = ExtractRawOggBytes(moggBytes);
        try
        {
            return await ExtractDecodedAsync(rawOggBytes, songInfo, outputDir, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            Dictionary<string, string> stems = new(StringComparer.OrdinalIgnoreCase);
            string backingPath = Path.Combine(outputDir, "song.ogg");
            await File.WriteAllBytesAsync(backingPath, rawOggBytes, cancellationToken).ConfigureAwait(false);
            stems["song"] = backingPath;
            return stems;
        }
    }

    private static async Task<IReadOnlyDictionary<string, string>> ExtractDecodedAsync(
        byte[] rawOggBytes,
        DtaSongInfo songInfo,
        string outputDir,
        CancellationToken cancellationToken)
    {
        (int sampleRate, float[][] decodedChannels) = DecodeVorbisChannels(rawOggBytes);
        if (decodedChannels.Length == 0 || decodedChannels[0].Length == 0)
        {
            throw new InvalidDataException("Decoded MOGG audio contained no samples.");
        }

        Dictionary<string, List<int>> normalisedTracks = BuildNormalisedTrackMap(songInfo.TrackChannels);
        Dictionary<string, string> stems = new(StringComparer.OrdinalIgnoreCase);

        List<int> usedChannels = [];
        foreach ((string stem, List<int> channels) in normalisedTracks)
        {
            if (string.Equals(stem, "crowd", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!ShouldIncludeStem(stem, songInfo.Ranks))
            {
                continue;
            }

            usedChannels.AddRange(channels);
        }

        if (normalisedTracks.TryGetValue("crowd", out List<int>? crowdChannels))
        {
            usedChannels.AddRange(crowdChannels);
        }

        var backingChannels = Enumerable.Range(0, decodedChannels.Length)
            .Where(index => !usedChannels.Contains(index))
            .ToList();
        if (backingChannels.Count == 0)
        {
            backingChannels = Enumerable.Range(0, decodedChannels.Length).ToList();
        }

        float[] backingStereo = MixChannelsToStereo(decodedChannels, backingChannels, songInfo.Pans, songInfo.Vols);
        string backingPath = Path.Combine(outputDir, "song.ogg");
        await WriteStereoVorbisAsync(backingPath, sampleRate, backingStereo, cancellationToken).ConfigureAwait(false);
        stems["song"] = backingPath;

        foreach ((string stem, List<int> channels) in normalisedTracks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.Equals(stem, "crowd", StringComparison.OrdinalIgnoreCase))
            {
                if (channels.Count == 0)
                {
                    continue;
                }
            }
            else if (!ShouldIncludeStem(stem, songInfo.Ranks))
            {
                continue;
            }

            var validChannels = channels
                .Where(channel => channel >= 0 && channel < decodedChannels.Length)
                .Distinct()
                .OrderBy(index => index)
                .ToList();
            if (validChannels.Count == 0)
            {
                continue;
            }

            float[] stereo = MixChannelsToStereo(decodedChannels, validChannels, songInfo.Pans, songInfo.Vols);
            if (IsSilent(stereo))
            {
                continue;
            }

            string stemPath = Path.Combine(outputDir, $"{stem}.ogg");
            await WriteStereoVorbisAsync(stemPath, sampleRate, stereo, cancellationToken).ConfigureAwait(false);
            stems[stem] = stemPath;
        }

        return stems;
    }

    private static Dictionary<string, List<int>> BuildNormalisedTrackMap(IReadOnlyDictionary<string, IReadOnlyList<int>> trackChannels)
    {
        Dictionary<string, List<int>> result = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string rawStem, IReadOnlyList<int> channels) in trackChannels)
        {
            string stem = NormaliseStemName(rawStem);
            if (!result.TryGetValue(stem, out List<int>? list))
            {
                list = [];
                result[stem] = list;
            }

            foreach (int channel in channels)
            {
                if (!list.Contains(channel))
                {
                    list.Add(channel);
                }
            }
        }

        return result;
    }

    private static string NormaliseStemName(string stem)
    {
        return stem.Trim().ToLowerInvariant() switch
        {
            "drum" or "drums" => "drums",
            "bass" or "rhythm" => "rhythm",
            "guitar" or "guitar_coop" or "guitarcoop" => "guitar",
            "vocals" or "vocal" => "vocals",
            "keys" => "keys",
            "crowd" => "crowd",
            _ => stem.Trim().ToLowerInvariant(),
        };
    }

    private static bool ShouldIncludeStem(string stem, IReadOnlyDictionary<string, int> ranks)
    {
        if (ranks.Count == 0)
        {
            return true;
        }

        return stem switch
        {
            "drums" => HasNonZeroRank(ranks, "drum", "drums"),
            "guitar" => HasNonZeroRank(ranks, "guitar", "guitar_coop", "guitarcoop"),
            "rhythm" => HasNonZeroRank(ranks, "bass", "rhythm"),
            "keys" => HasNonZeroRank(ranks, "keys"),
            "vocals" => HasNonZeroRank(ranks, "vocals", "vocal"),
            "crowd" => true,
            _ => HasNonZeroRank(ranks, stem),
        };
    }

    private static bool HasNonZeroRank(IReadOnlyDictionary<string, int> ranks, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (ranks.TryGetValue(key, out int rank) && rank != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static (int SampleRate, float[][] Channels) DecodeVorbisChannels(byte[] oggBytes)
    {
        if (TryDecodeVorbisChannelsWithStb(oggBytes, out (int SampleRate, float[][] Channels) decodedWithStb))
        {
            return decodedWithStb;
        }

        return DecodeVorbisChannelsWithNVorbis(oggBytes);
    }

    private static bool TryDecodeVorbisChannelsWithStb(byte[] oggBytes, out (int SampleRate, float[][] Channels) decoded)
    {
        decoded = default;

        try
        {
            short[] interleavedPcm = StbVorbis.decode_vorbis_from_memory(oggBytes, out int sampleRate, out int channelCount);

            // StbVorbisSharp returns (sampleRate, channels) via out args.
            if (sampleRate <= 0 || channelCount <= 0 || interleavedPcm.Length == 0)
            {
                return false;
            }

            int frameCount = interleavedPcm.Length / channelCount;
            if (frameCount <= 0)
            {
                return false;
            }

            float[][] perChannel = new float[channelCount][];
            for (int channel = 0; channel < channelCount; channel++)
            {
                perChannel[channel] = new float[frameCount];
            }

            for (int frame = 0; frame < frameCount; frame++)
            {
                int baseIndex = frame * channelCount;
                for (int channel = 0; channel < channelCount; channel++)
                {
                    perChannel[channel][frame] = interleavedPcm[baseIndex + channel] / 32768f;
                }
            }

            decoded = (sampleRate, perChannel);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static (int SampleRate, float[][] Channels) DecodeVorbisChannelsWithNVorbis(byte[] oggBytes)
    {
        using var stream = new MemoryStream(oggBytes, writable: false);
        using var reader = new VorbisReader(stream, closeOnDispose: false);

        int sourceChannels = reader.Channels;
        if (sourceChannels <= 0)
        {
            throw new InvalidDataException("OGG stream reports no channels.");
        }

        List<float>[] perChannel = Enumerable.Range(0, sourceChannels)
            .Select(_ => new List<float>(8192))
            .ToArray();

        float[] chunk = new float[4096 * sourceChannels];
        while (true)
        {
            int sampleCount = reader.ReadSamples(chunk, 0, chunk.Length);
            if (sampleCount <= 0)
            {
                break;
            }

            int frameCount = sampleCount / sourceChannels;
            for (int frame = 0; frame < frameCount; frame++)
            {
                int baseIndex = frame * sourceChannels;
                for (int channel = 0; channel < sourceChannels; channel++)
                {
                    perChannel[channel].Add(chunk[baseIndex + channel]);
                }
            }
        }

        return (reader.SampleRate, perChannel.Select(list => list.ToArray()).ToArray());
    }

    private static float[] MixChannelsToStereo(
        float[][] sourceChannels,
        IReadOnlyList<int> indexes,
        IReadOnlyList<float> pans,
        IReadOnlyList<float> vols)
    {
        int frameCount = sourceChannels[0].Length;
        float[] output = new float[frameCount * 2];

        for (int frame = 0; frame < frameCount; frame++)
        {
            double left = 0;
            double right = 0;

            foreach (int index in indexes)
            {
                float sample = sourceChannels[index][frame];
                float pan = index < pans.Count ? pans[index] : 0;
                float volDb = index < vols.Count ? vols[index] : 0;
                double gain = Math.Pow(10, volDb / 20.0);
                (double panL, double panR) = StereoPanRatios(pan);

                left += sample * gain * panL;
                right += sample * gain * panR;
            }

            output[frame * 2] = (float)Math.Clamp(left, -1.0, 1.0);
            output[(frame * 2) + 1] = (float)Math.Clamp(right, -1.0, 1.0);
        }

        return output;
    }

    private static (double Left, double Right) StereoPanRatios(float pan)
    {
        double theta = pan * (Math.PI / 4.0);
        double factor = Math.Sqrt(2.0) / 2.0;
        double left = factor * (Math.Cos(theta) - Math.Sin(theta));
        double right = factor * (Math.Cos(theta) + Math.Sin(theta));
        return (left, right);
    }

    private static bool IsSilent(float[] interleavedStereo)
    {
        for (int i = 0; i < interleavedStereo.Length; i++)
        {
            if (Math.Abs(interleavedStereo[i]) > 0.0001f)
            {
                return false;
            }
        }

        return true;
    }

    private static async Task WriteStereoVorbisAsync(
        string path,
        int sampleRate,
        float[] interleavedStereo,
        CancellationToken cancellationToken)
    {
        var info = VorbisInfo.InitVariableBitRate(2, sampleRate, 0.2f);
        Comments comments = new();
        comments.AddTag("ENCODER", "ChartHub");

        OggStream stream = new(1);
        using FileStream output = File.Create(path);

        stream.PacketIn(HeaderPacketBuilder.BuildInfoPacket(info));
        stream.PacketIn(HeaderPacketBuilder.BuildCommentsPacket(comments));
        stream.PacketIn(HeaderPacketBuilder.BuildBooksPacket(info));
        while (stream.PageOut(out OggPage? headerPage, true))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await output.WriteAsync(headerPage.Header, cancellationToken).ConfigureAwait(false);
            await output.WriteAsync(headerPage.Body, cancellationToken).ConfigureAwait(false);
        }

        var processing = ProcessingState.Create(info);
        int totalFrames = interleavedStereo.Length / 2;
        const int framesPerChunk = 1024;
        for (int offset = 0; offset < totalFrames; offset += framesPerChunk)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int frameCount = Math.Min(framesPerChunk, totalFrames - offset);
            float[][] chunk =
            [
                new float[frameCount],
                new float[frameCount],
            ];

            for (int frame = 0; frame < frameCount; frame++)
            {
                int sourceIndex = (offset + frame) * 2;
                chunk[0][frame] = interleavedStereo[sourceIndex];
                chunk[1][frame] = interleavedStereo[sourceIndex + 1];
            }

            processing.WriteData(chunk, frameCount);
            await FlushEncodedPacketsAsync(processing, stream, output, cancellationToken, forcePageOut: false).ConfigureAwait(false);
        }

        processing.WriteEndOfStream();
        await FlushEncodedPacketsAsync(processing, stream, output, cancellationToken, forcePageOut: true).ConfigureAwait(false);
        while (stream.PageOut(out OggPage? page, true))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await output.WriteAsync(page.Header, cancellationToken).ConfigureAwait(false);
            await output.WriteAsync(page.Body, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task FlushEncodedPacketsAsync(
        ProcessingState processing,
        OggStream stream,
        FileStream output,
        CancellationToken cancellationToken,
        bool forcePageOut)
    {
        while (processing.PacketOut(out OggPacket? packet))
        {
            stream.PacketIn(packet);
            while (stream.PageOut(out OggPage? page, forcePageOut || packet.EndOfStream))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await output.WriteAsync(page.Header, cancellationToken).ConfigureAwait(false);
                await output.WriteAsync(page.Body, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // HMX private key for MOGG version 0x0B (Rock Band 1 / RB1 DLC).
    // This is a well-known community-documented constant, identical across all 0x0B-encrypted MOGG files.
    private static readonly byte[] HmxPrivateKey0B =
    [
        0x37, 0xB2, 0xE2, 0xB9, 0x1C, 0x74, 0xFA, 0x9E,
        0x38, 0x81, 0x08, 0xEA, 0x36, 0x23, 0xDB, 0xE4,
    ];

    // For 0x0D files, the actual stream key is derived at runtime and not fixed.
    private static readonly byte[] HmxHvKey0 =
    [
        0x01, 0x22, 0x00, 0x38, 0xD2, 0x01, 0x78, 0x8B,
        0xDD, 0xCD, 0xD0, 0xF0, 0xFE, 0x3E, 0x24, 0x7F,
    ];

    private static readonly byte[] C3BadMask =
    [
        0xC3, 0xC3, 0xC3, 0xC3, 0x00, 0x01, 0x02, 0x03,
        0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B,
    ];

    private static readonly byte[] C3GoodMask =
    [
        0xA5, 0xCE, 0xFD, 0x06, 0x11, 0x93, 0x23, 0x21,
        0xF8, 0x87, 0x85, 0xEA, 0x95, 0xE4, 0x94, 0xD4,
    ];

    private static readonly byte[] HiddenKeys =
    [
        0x7F, 0x95, 0x5B, 0x9D, 0x94, 0xBA, 0x12, 0xF1, 0xD7, 0x5A, 0x67, 0xD9, 0x16, 0x45, 0x28, 0xDD,
        0x61, 0x55, 0x55, 0xAF, 0x23, 0x91, 0xD6, 0x0A, 0x3A, 0x42, 0x81, 0x18, 0xB4, 0xF7, 0xF3, 0x04,
        0x78, 0x96, 0x5D, 0x92, 0x92, 0xB0, 0x47, 0xAC, 0x8F, 0x5B, 0x6D, 0xDC, 0x1C, 0x41, 0x7E, 0xDA,
        0x6A, 0x55, 0x53, 0xAF, 0x20, 0xC8, 0xDC, 0x0A, 0x66, 0x43, 0xDD, 0x1C, 0xB2, 0xA5, 0xA4, 0x0C,
        0x7E, 0x92, 0x5C, 0x93, 0x90, 0xED, 0x4A, 0xAD, 0x8B, 0x07, 0x36, 0xD3, 0x10, 0x41, 0x78, 0x8F,
        0x60, 0x08, 0x55, 0xA8, 0x26, 0xCF, 0xD0, 0x0F, 0x65, 0x11, 0x84, 0x45, 0xB1, 0xA0, 0xFA, 0x57,
        0x79, 0x97, 0x0B, 0x90, 0x92, 0xB0, 0x44, 0xAD, 0x8A, 0x0E, 0x60, 0xD9, 0x14, 0x11, 0x7E, 0x8D,
        0x35, 0x5D, 0x5C, 0xFB, 0x21, 0x9C, 0xD3, 0x0E, 0x32, 0x40, 0xD1, 0x48, 0xB8, 0xA7, 0xA1, 0x0D,
        0x28, 0xC3, 0x5D, 0x97, 0xC1, 0xEC, 0x42, 0xF1, 0xDC, 0x5D, 0x37, 0xDA, 0x14, 0x47, 0x79, 0x8A,
        0x32, 0x5C, 0x54, 0xF2, 0x72, 0x9D, 0xD3, 0x0D, 0x67, 0x4C, 0xD6, 0x49, 0xB4, 0xA2, 0xF3, 0x50,
        0x28, 0x96, 0x5E, 0x95, 0xC5, 0xE9, 0x45, 0xAD, 0x8A, 0x5D, 0x64, 0x8E, 0x17, 0x40, 0x2E, 0x87,
        0x36, 0x58, 0x06, 0xFD, 0x75, 0x90, 0xD0, 0x5F, 0x3A, 0x40, 0xD4, 0x4C, 0xB0, 0xF7, 0xA7, 0x04,
        0x2C, 0x96, 0x01, 0x96, 0x9B, 0xBC, 0x15, 0xA6, 0xDE, 0x0E, 0x65, 0x8D, 0x17, 0x47, 0x2F, 0xDD,
        0x63, 0x54, 0x55, 0xAF, 0x76, 0xCA, 0x84, 0x5F, 0x62, 0x44, 0x80, 0x4A, 0xB3, 0xF4, 0xF4, 0x0C,
        0x7E, 0xC4, 0x0E, 0xC6, 0x9A, 0xEB, 0x43, 0xA0, 0xDB, 0x0A, 0x64, 0xDF, 0x1C, 0x42, 0x24, 0x89,
        0x63, 0x5C, 0x55, 0xF3, 0x71, 0x90, 0xDC, 0x5D, 0x60, 0x40, 0xD1, 0x4D, 0xB2, 0xA3, 0xA7, 0x0D,
        0x2C, 0x9A, 0x0B, 0x90, 0x9A, 0xBE, 0x47, 0xA7, 0x88, 0x5A, 0x6D, 0xDF, 0x13, 0x1D, 0x2E, 0x8B,
        0x60, 0x5E, 0x55, 0xF2, 0x74, 0x9C, 0xD7, 0x0E, 0x60, 0x40, 0x80, 0x1C, 0xB7, 0xA1, 0xF4, 0x02,
        0x28, 0x96, 0x5B, 0x95, 0xC1, 0xE9, 0x40, 0xA3, 0x8F, 0x0C, 0x32, 0xDF, 0x43, 0x1D, 0x24, 0x8D,
        0x61, 0x09, 0x54, 0xAB, 0x27, 0x9A, 0xD3, 0x58, 0x60, 0x16, 0x84, 0x4F, 0xB3, 0xA4, 0xF3, 0x0D,
        0x25, 0x93, 0x08, 0xC0, 0x9A, 0xBD, 0x10, 0xA2, 0xD6, 0x09, 0x60, 0x8F, 0x11, 0x1D, 0x7A, 0x8F,
        0x63, 0x0B, 0x5D, 0xF2, 0x21, 0xEC, 0xD7, 0x08, 0x62, 0x40, 0x84, 0x49, 0xB0, 0xAD, 0xF2, 0x07,
        0x29, 0xC3, 0x0C, 0x96, 0x96, 0xEB, 0x10, 0xA0, 0xDA, 0x59, 0x32, 0xD3, 0x17, 0x41, 0x25, 0xDC,
        0x63, 0x08, 0x04, 0xAE, 0x77, 0xCB, 0x84, 0x5A, 0x60, 0x4D, 0xDD, 0x45, 0xB5, 0xF4, 0xA0, 0x05,
    ];

    /// <summary>
    /// Returns the raw, unencrypted OGG bytes from a MOGG file, decrypting if the version byte
    /// indicates an encrypted variant.
    /// </summary>
    private static byte[] ExtractRawOggBytes(byte[] moggBytes)
    {
        if (moggBytes.Length < 4)
        {
            throw new InvalidDataException("MOGG file is too short to contain a version header.");
        }

        int version = moggBytes[0] | (moggBytes[1] << 8) | (moggBytes[2] << 16) | (moggBytes[3] << 24);

        return version switch
        {
            0x0A => moggBytes[ReadMoggOggOffset(moggBytes)..],
            0x0B => DecryptMogg0x0B(moggBytes),
            0x0D => DecryptMogg0x0D(moggBytes),
            _ => RecoverRawOggFromUnknownHeader(moggBytes, version),
        };

    }

    /// <summary>
    /// Decrypts the OGG payload from a MOGG version 0x0D file (C3 custom format).
    /// </summary>
    /// <remarks>
    /// Header layout for 0x0D:
    ///   [0..3]   version (0x0D LE)
    ///   [4..7]   OGG data offset LE
    ///   [8..11]  OGG map version LE
    ///   [12..15] buffer size LE
    ///   [16..19] seek-table entry count N LE
    ///   [20..20+N*8-1] seek table
    ///   [20+N*8..20+N*8+71] PUBLIC_KEY (72 bytes; includes counter seed and key-derivation metadata)
    ///   [OGG data offset..] OGG data (encrypted with AES-CTR using derived key)
    /// The decrypted stream may begin with "HMXA" instead of "OggS"; if so the serial
    /// (bytes 0x0C–0x0F) and checksum (bytes 0x14–0x17) must be recomputed from the
    /// PUBLIC_KEY bytes before the result is a valid Ogg bitstream.
    /// </remarks>
    private static byte[] DecryptMogg0x0D(byte[] moggBytes)
    {
        if (moggBytes.Length < 20)
        {
            throw new InvalidDataException("0x0D MOGG file is too short to read the seek-table entry count.");
        }

        byte[] patchedBytes = (byte[])moggBytes.Clone();
        TryPatchOldC3Mask(patchedBytes);

        int numPairs = patchedBytes[16] | (patchedBytes[17] << 8) | (patchedBytes[18] << 16) | (patchedBytes[19] << 24);
        int publicKeyOffset = 20 + (numPairs * 8);

        // PUBLIC_KEY for 0x0D is 72 bytes; first 16 bytes are the AES counter seed.
        if (patchedBytes.Length < publicKeyOffset + 72)
        {
            throw new InvalidDataException(
                "0x0D MOGG file is too short to contain the 72-byte PUBLIC_KEY after the seek table.");
        }

        byte[] publicKey = patchedBytes[publicKeyOffset..(publicKeyOffset + 72)];
        int oggDataStart = patchedBytes[4] | (patchedBytes[5] << 8) | (patchedBytes[6] << 16) | (patchedBytes[7] << 24);
        byte[] encryptedOgg = patchedBytes[oggDataStart..];

        byte[] ctrKey = DeriveCtrKey0x0D(publicKey);
        byte[] decryptedOgg = DecryptMoggAesCtr(encryptedOgg, ctrKey, publicKey[..16]);

        // C3-encrypted MOGGs may start with HMXA instead of OggS. Fix the header if so.
        if (decryptedOgg.Length >= 4
            && decryptedOgg[0] == 0x48 && decryptedOgg[1] == 0x4D
            && decryptedOgg[2] == 0x58 && decryptedOgg[3] == 0x41)
        {
            FixHmxaHeader(decryptedOgg, publicKey);
        }

        return decryptedOgg;
    }

    private static void TryPatchOldC3Mask(byte[] moggBytes)
    {
        if (moggBytes.Length < 20)
        {
            return;
        }

        int numPairs = moggBytes[16] | (moggBytes[17] << 8) | (moggBytes[18] << 16) | (moggBytes[19] << 24);
        int patchLocation = 20 + (numPairs * 8) + 16 + 16;

        if (patchLocation < 0 || patchLocation + 16 > moggBytes.Length)
        {
            return;
        }

        if (MatchesAt(moggBytes, patchLocation, C3BadMask))
        {
            Buffer.BlockCopy(C3GoodMask, 0, moggBytes, patchLocation, C3GoodMask.Length);
        }
    }

    private static bool MatchesAt(byte[] data, int offset, byte[] pattern)
    {
        if (offset < 0 || offset + pattern.Length > data.Length)
        {
            return false;
        }

        for (int i = 0; i < pattern.Length; i++)
        {
            if (data[offset + i] != pattern[i])
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] DeriveCtrKey0x0D(byte[] publicKey)
    {
        if (publicKey.Length < 72)
        {
            throw new InvalidDataException("0x0D PUBLIC_KEY block must be 72 bytes.");
        }

        uint seed1 = (uint)(publicKey[16] | (publicKey[17] << 8) | (publicKey[18] << 16) | (publicKey[19] << 24));
        uint seed2 = (uint)(publicKey[24] | (publicKey[25] << 8) | (publicKey[26] << 16) | (publicKey[27] << 24));
        uint keyIndexRaw = (uint)(publicKey[64] | (publicKey[65] << 8) | (publicKey[66] << 16) | (publicKey[67] << 24));
        int keyIndex = (int)(keyIndexRaw % 6) + 6;

        byte[] encryptedFileKey = publicKey[48..64];
        byte[] decryptedFileKey = AesEcbDecryptBlock(encryptedFileKey, HmxHvKey0);

        byte[] masterKey = BuildMasterMasher();
        byte[] tempKey = GetKeyFromChain(keyIndex, masterKey);
        GrindArray(seed1, seed2, tempKey);

        byte[] ctrKey = new byte[16];
        for (int i = 0; i < 16; i++)
        {
            ctrKey[i] = (byte)(tempKey[i] ^ decryptedFileKey[i]);
        }

        return ctrKey;
    }

    private static byte[] AesEcbDecryptBlock(byte[] block, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        using ICryptoTransform decryptor = aes.CreateDecryptor();
        byte[] output = new byte[16];
        decryptor.TransformBlock(block, 0, 16, output, 0);
        return output;
    }

    private static byte[] BuildMasterMasher()
    {
        byte[] result = new byte[32];
        int state = 0xEB;

        for (int i = 0; i < 8; i++)
        {
            if (i == 0)
            {
                state = 0xEB;
            }

            unchecked
            {
                state = (state * 0x19660E) + 0x3C6EF35F;
            }

            byte[] word = BitConverter.GetBytes(state);
            Buffer.BlockCopy(word, 0, result, i * 4, 4);
        }

        return result;
    }

    private static byte[] GetKeyFromChain(int keyIndex, byte[] masterKey)
    {
        byte[] revealed = new byte[32];
        Buffer.BlockCopy(HiddenKeys, keyIndex * 32, revealed, 0, 32);

        for (int c = 14; c > 0; c--)
        {
            SuperShuffle(revealed);
        }

        for (int i = 0; i < 32; i++)
        {
            revealed[i] ^= masterKey[i];
        }

        byte[] key = new byte[16];
        for (int i = 0; i < 16; i++)
        {
            key[i] = (byte)((AsciiDigitToHex(revealed[i * 2]) << 4) | AsciiDigitToHex(revealed[(i * 2) + 1]));
        }

        return key;
    }

    private static int AsciiDigitToHex(byte value)
    {
        if (value >= (byte)'a' && value <= (byte)'f')
        {
            return value - (byte)'a' + 10;
        }

        if (value >= (byte)'A' && value <= (byte)'F')
        {
            return value - (byte)'A' + 10;
        }

        if (value >= (byte)'0' && value <= (byte)'9')
        {
            return value - (byte)'0';
        }

        return 0;
    }

    private static void SuperShuffle(byte[] buffer)
    {
        Shuffle1(buffer);
        Shuffle2(buffer);
        Shuffle3(buffer);
        Shuffle4(buffer);
        Shuffle5(buffer);
        Shuffle6(buffer);
    }

    private static int Roll(int arg) => (arg + 19) % 32;

    private static void Swap(byte[] buffer, int a, int b)
    {
        byte tmp = buffer[a];
        buffer[a] = buffer[b];
        buffer[b] = tmp;
    }

    private static void Shuffle1(byte[] buffer)
    {
        for (int i = 0; i < 8; i++)
        {
            Swap(buffer, Roll(i * 4), (i * 4) + 2);
            Swap(buffer, Roll((i * 4) + 3), (i * 4) + 1);
        }
    }

    private static void Shuffle2(byte[] buffer)
    {
        for (int i = 0; i < 8; i++)
        {
            Swap(buffer, 29 - (i * 4), (i * 4) + 2);
            Swap(buffer, 28 - (i * 4), (i * 4) + 3);
        }
    }

    private static void Shuffle3(byte[] buffer)
    {
        for (int i = 0; i < 8; i++)
        {
            Swap(buffer, Roll((4 * (7 - i)) + 1), (i * 4) + 2);
            Swap(buffer, 4 * (7 - i), (i * 4) + 3);
        }
    }

    private static void Shuffle4(byte[] buffer)
    {
        for (int i = 0; i < 8; i++)
        {
            Swap(buffer, 29 - (i * 4), (i * 4) + 2);
            Swap(buffer, Roll(4 * (7 - i)), (i * 4) + 3);
        }
    }

    private static void Shuffle5(byte[] buffer)
    {
        for (int i = 0; i < 8; i++)
        {
            Swap(buffer, 29 - (i * 4), Roll((i * 4) + 2));
            Swap(buffer, 28 - (i * 4), (i * 4) + 3);
        }
    }

    private static void Shuffle6(byte[] buffer)
    {
        for (int i = 0; i < 8; i++)
        {
            Swap(buffer, 29 - (i * 4), (i * 4) + 2);
            Swap(buffer, 28 - (i * 4), Roll((i * 4) + 3));
        }
    }

    private static void GrindArray(uint seed1, uint seed2, byte[] key)
    {
        Span<byte> hashMap = stackalloc byte[256];
        uint seed = seed1;
        for (int i = 0; i < 256; i++)
        {
            hashMap[i] = (byte)((seed >> 3) & 0x1F);
            unchecked
            {
                seed = (0x19660Du * seed) + 0x3C6EF35Fu;
            }
        }

        int[] opMap = BuildRb2OpMap();
        int[] switchCases = BuildSwitchCases(seed2);

        for (int i = 0; i < key.Length; i++)
        {
            byte foo = key[i];
            for (int ix = 0; ix < key.Length; ix += 2)
            {
                int opIndex = opMap[switchCases[hashMap[key[ix]]]];
                foo = ApplyRb2Operation(opIndex, key[ix + 1], foo);
            }

            key[i] = foo;
        }
    }

    private static int[] BuildRb2OpMap()
    {
        int[] map = new int[32];
        bool[] used = new bool[32];
        uint seed = 0xD5;

        for (int i = 0; i < 32; i++)
        {
            int slot;
            do
            {
                unchecked
                {
                    seed = (seed * 0x19660Du) + 0x3C6EF35Fu;
                }

                slot = (int)((seed >> 2) & 0x1F);
            }
            while (used[slot]);

            used[slot] = true;
            map[slot] = i;
        }

        return map;
    }

    private static int[] BuildSwitchCases(uint seed)
    {
        int[] cases = new int[32];
        bool[] used = new bool[32];

        for (int i = 0; i < 32; i++)
        {
            int slot;
            do
            {
                unchecked
                {
                    seed = (seed * 0x19660Du) + 0x3C6EF35Fu;
                }

                slot = (int)((seed >> 2) & 0x1F);
            }
            while (used[slot]);

            used[slot] = true;
            cases[i] = slot;
        }

        return cases;
    }

    private static byte RotateRight(byte value, int amount)
    {
        amount &= 7;
        int input = value;
        return (byte)((input >> amount) | (input << (8 - amount)));
    }

    private static byte ApplyRb2Operation(int opIndex, byte bar, byte foo)
    {
        unchecked
        {
            return opIndex switch
            {
                0 => (byte)(foo ^ bar),
                1 => (byte)(foo + bar),
                2 => RotateRight(foo, bar & 7),
                3 => RotateRight(foo, bar == 0 ? 1 : 0),
                4 => RotateRight((byte)(foo == 0 ? 1 : 0), bar == 0 ? 1 : 0),
                5 => RotateRight((byte)(0xFF ^ foo), bar & 7),
                6 => (byte)(bar ^ (foo == 0 ? 1 : 0)),
                7 => (byte)(bar + (foo == 0 ? 1 : 0)),
                8 => (byte)(bar ^ (foo + bar)),
                9 => (byte)(bar + (foo ^ bar)),
                10 => (byte)(bar ^ RotateRight(foo, bar == 0 ? 1 : 0)),
                11 => (byte)(bar ^ RotateRight(foo, bar & 7)),
                12 => (byte)(bar + RotateRight(foo, bar & 7)),
                13 => (byte)(bar + RotateRight(foo, bar == 0 ? 1 : 0)),
                14 => (byte)(bar + RotateRight(foo, 1)),
                15 => (byte)(bar + RotateRight(foo, 2)),
                16 => (byte)(bar + RotateRight(foo, 3)),
                17 => (byte)(bar + RotateRight(foo, 4)),
                18 => (byte)(bar + RotateRight(foo, 5)),
                19 => (byte)(bar + RotateRight(foo, 6)),
                20 => (byte)(bar + RotateRight(foo, 7)),
                21 => (byte)(bar ^ RotateRight(foo, 1)),
                22 => (byte)(bar ^ RotateRight(foo, 2)),
                23 => (byte)(bar ^ RotateRight(foo, 3)),
                24 => (byte)(bar ^ RotateRight(foo, 4)),
                25 => (byte)(bar ^ RotateRight(foo, 5)),
                26 => (byte)(bar ^ RotateRight(foo, 6)),
                27 => (byte)(bar ^ RotateRight(foo, 7)),
                28 => (byte)(bar ^ (bar + RotateRight(foo, 5))),
                29 => (byte)(bar ^ (bar + RotateRight(foo, 3))),
                30 => (byte)(bar + (bar ^ RotateRight(foo, 3))),
                31 => (byte)(bar + (bar ^ RotateRight(foo, 5))),
                _ => foo,
            };
        }
    }

    /// <summary>
    /// Converts an HMXA-prefixed decrypted OGG stream into a valid OggS stream.
    /// Rewrites bytes 0–3 (magic), 0x0C–0x0F (serial), and 0x14–0x17 (checksum).
    /// </summary>
    private static void FixHmxaHeader(byte[] oggData, byte[] publicKey)
    {
        // Replace HMXA → OggS magic.
        oggData[0] = (byte)'O';
        oggData[1] = (byte)'g';
        oggData[2] = (byte)'g';
        oggData[3] = (byte)'S';

        if (oggData.Length < 0x18 || publicKey.Length < 0x1C)
        {
            return;
        }

        // Match the original C3 Tools byte-order semantics exactly.
        // Serial/checksum are read from the HMXA header in reverse order,
        // transformed, then written back as reversed bytes.
        uint mogg0x10 = (uint)(publicKey[0x10] | (publicKey[0x11] << 8) | (publicKey[0x12] << 16) | (publicKey[0x13] << 24));
        uint serial = (uint)(oggData[0x0F] | (oggData[0x0E] << 8) | (oggData[0x0D] << 16) | (oggData[0x0C] << 24));
        unchecked
        {
            uint serialCrypt = (mogg0x10 ^ 0x5c5c5c5c) * (0x00190000 + 0x0000660d) + 0x3c6f0000 - 0xca1;
            serialCrypt = (serialCrypt * (0x00190000 + 0x660d)) + 0x3c6f0000 - 0xca1;
            serialCrypt ^= serial;
            byte[] serialLe = BitConverter.GetBytes(serialCrypt);
            byte[] serialBytes = [serialLe[3], serialLe[2], serialLe[1], serialLe[0]];
            serialBytes.CopyTo(oggData, 0x0C);
        }

        // ChecksumCrypt transform (uses PUBLIC_KEY bytes 0x18–0x1B).
        uint mogg0x18 = (uint)(publicKey[0x18] | (publicKey[0x19] << 8) | (publicKey[0x1A] << 16) | (publicKey[0x1B] << 24));
        uint checksum = (uint)(oggData[0x17] | (oggData[0x16] << 8) | (oggData[0x15] << 16) | (oggData[0x14] << 24));
        unchecked
        {
            uint checksumCrypt = (mogg0x18 ^ 0x36363636) * (0x00190000 + 0x0000660d) + 0x3c6f0000 - 0xca1;
            checksumCrypt ^= checksum;
            byte[] checksumLe = BitConverter.GetBytes(checksumCrypt);
            byte[] checksumBytes = [checksumLe[3], checksumLe[2], checksumLe[1], checksumLe[0]];
            checksumBytes.CopyTo(oggData, 0x14);
        }
    }

    private static byte[] RecoverRawOggFromUnknownHeader(byte[] moggBytes, int version)
    {
        // Some fan-made packages can contain malformed MOGG headers while still embedding
        // a valid Ogg stream in the payload. If an OggS sync word exists, recover from it.
        for (int i = 0; i <= moggBytes.Length - 4; i++)
        {
            if (IsOggSyncAt(moggBytes, i))
            {
                return moggBytes[i..];
            }
        }

        throw new NotSupportedException(
            $"MOGG encryption version 0x{version:X2} is not supported. " +
                "Only unencrypted (0x0A), RB1-style encrypted (0x0B), and C3 custom (0x0D) MOGG files are handled.");
    }

    /// <summary>
    /// Decrypts the OGG payload from a MOGG version 0x0B file.
    /// Version 0x0B uses AES-ECB as a counter-mode stream cipher (the same scheme as RB1/RB1 DLC).
    /// </summary>
    /// <remarks>
    /// Header layout for 0x0B:
    ///   [0..3]   version (0x0B LE)
    ///   [4..7]   OGG data offset (= header size) LE
    ///   [8..11]  OGG map version LE
    ///   [12..15] buffer size LE
    ///   [16..19] seek-table entry count N LE
    ///   [20..20+N*8-1] seek table (N × offset/value pairs, 8 bytes each)
    ///   [20+N*8..20+N*8+15] PUBLIC_KEY (16 bytes, functions as the AES counter seed)
    ///   [20+N*8+16..] OGG data (encrypted)
    /// </remarks>
    private static byte[] DecryptMogg0x0B(byte[] moggBytes)
    {
        if (moggBytes.Length < 20)
        {
            throw new InvalidDataException("0x0B MOGG file is too short to read the seek-table entry count.");
        }

        int numPairs = moggBytes[16] | (moggBytes[17] << 8) | (moggBytes[18] << 16) | (moggBytes[19] << 24);
        int publicKeyOffset = 20 + (numPairs * 8);

        if (moggBytes.Length < publicKeyOffset + 16)
        {
            throw new InvalidDataException(
                "0x0B MOGG file is too short to contain the 16-byte PUBLIC_KEY after the seek table.");
        }

        byte[] publicKey = moggBytes[publicKeyOffset..(publicKeyOffset + 16)];
        int oggDataStart = publicKeyOffset + 16;
        byte[] encryptedOgg = moggBytes[oggDataStart..];

        return DecryptMoggAesCtr(encryptedOgg, HmxPrivateKey0B, publicKey);
    }

    /// <summary>
    /// AES-ECB counter-mode decryption as used by Harmonix MOGG files.
    /// The <paramref name="initialCounter"/> seeds the 16-byte counter block (AES input).
    /// After every 16 keystream bytes consumed, the counter is incremented little-endian and a new
    /// 16-byte keystream block is generated. Data is XOR'd byte-by-byte against the keystream.
    /// </summary>
    private static byte[] DecryptMoggAesCtr(byte[] data, byte[] key, byte[] initialCounter)
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
                // Increment counter little-endian (same carry logic as Harmonix MoggCrypt).
                for (int j = 0; j < 16; j++)
                {
                    counter[j]++;
                    if (counter[j] != 0)
                    {
                        break;
                    }
                }

                encryptor.TransformBlock(counter, 0, 16, keystream, 0);
                blockOffset = 0;
            }

            result[i] = (byte)(data[i] ^ keystream[blockOffset]);
            blockOffset++;
        }

        return result;
    }

    /// <summary>Reads the OGG data offset from the MOGG header.</summary>
    private static int ReadMoggOggOffset(byte[] bytes)
    {
        if (bytes.Length < 8)
        {
            throw new InvalidDataException("MOGG file is too short to contain a valid header.");
        }

        // Bytes [4–7]: OGG start offset, little-endian 32-bit.
        int offset = bytes[4] | (bytes[5] << 8) | (bytes[6] << 16) | (bytes[7] << 24);
        if (IsOggSyncAt(bytes, offset))
        {
            return offset;
        }

        // Common fallback for older containers.
        if (IsOggSyncAt(bytes, 8))
        {
            return 8;
        }

        // Some files carry a stale/incorrect offset in the header; recover by scanning.
        for (int i = 0; i <= bytes.Length - 4; i++)
        {
            if (IsOggSyncAt(bytes, i))
            {
                return i;
            }
        }

        throw new InvalidDataException("MOGG payload does not contain an OggS sync word.");
    }

    private static bool IsOggSyncAt(byte[] bytes, int offset)
    {
        return offset >= 0
            && offset <= bytes.Length - 4
            && bytes[offset] == (byte)'O'
            && bytes[offset + 1] == (byte)'g'
            && bytes[offset + 2] == (byte)'g'
            && bytes[offset + 3] == (byte)'S';
    }

}
