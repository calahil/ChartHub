using System.Security.Cryptography;

using ChartHub.Conversion.Dta;

using NVorbis;

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
    /// Extracts backing audio into <paramref name="outputDir"/>.
    /// </summary>
    /// <param name="moggBytes">Raw MOGG file bytes.</param>
    /// <param name="songInfo">DTA metadata providing channel-to-stem mapping.</param>
    /// <param name="outputDir">Directory where stem OGGs will be written.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Map containing at least the backing track key "song".</returns>
    public async Task<IReadOnlyDictionary<string, string>> ExtractStemsAsync(
        byte[] moggBytes,
        DtaSongInfo songInfo,
        string outputDir,
        CancellationToken cancellationToken = default)
    {
        byte[] rawOggBytes = ExtractRawOggBytes(moggBytes);
        var stems = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Materialize backing audio directly from recovered OGG bytes.
        string backingPath = Path.Combine(outputDir, "song.ogg");
        await File.WriteAllBytesAsync(backingPath, rawOggBytes, cancellationToken).ConfigureAwait(false);
        stems["song"] = backingPath;

        if (songInfo.TrackChannels.Count == 0)
        {
            return stems;
        }

        int sampleRate;
        float[][] decodedChannels;
        try
        {
            (sampleRate, decodedChannels) = DecodeVorbisChannels(rawOggBytes);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            // If decode fails, preserve deterministic backing-only output.
            return stems;
        }

        if (decodedChannels.Length == 0 || decodedChannels[0].Length == 0)
        {
            return stems;
        }

        Dictionary<string, List<int>> normalisedTracks = BuildNormalisedTrackMap(songInfo.TrackChannels);
        foreach ((string stem, List<int> channels) in normalisedTracks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var validChannels = channels
                .Where(channel => channel >= 0 && channel < decodedChannels.Length)
                .Distinct()
                .ToList();

            if (validChannels.Count == 0)
            {
                continue;
            }

            float[] stereo = MixChannelsToStereo(decodedChannels, validChannels, songInfo.Pans, songInfo.Vols);
            if (stereo.Length == 0)
            {
                continue;
            }

            string stemFile = Path.Combine(outputDir, $"{stem}.wav");
            await WriteStereoWavAsync(stemFile, sampleRate, stereo, cancellationToken).ConfigureAwait(false);
            stems[stem] = stemFile;
        }

        return stems;
    }

    private static Dictionary<string, List<int>> BuildNormalisedTrackMap(
        IReadOnlyDictionary<string, IReadOnlyList<int>> trackChannels)
    {
        Dictionary<string, List<int>> result = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string stem, IReadOnlyList<int> channels) in trackChannels)
        {
            string key = NormaliseStemName(stem);
            if (!result.TryGetValue(key, out List<int>? list))
            {
                list = [];
                result[key] = list;
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
        return stem.ToLowerInvariant() switch
        {
            "drum" or "drums" => "drums",
            "bass" => "rhythm",
            "rhythm" => "rhythm",
            "guitar" => "guitar",
            "vocals" or "vocal" => "vocals",
            "keys" => "keys",
            "crowd" => "crowd",
            _ => stem.ToLowerInvariant(),
        };
    }

    private static (int SampleRate, float[][] Channels) DecodeVorbisChannels(byte[] oggBytes)
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
        // Match Onyx constant-power panning math used in applyPansVols.
        double theta = pan * (Math.PI / 4.0);
        double factor = Math.Sqrt(2.0) / 2.0;
        double left = factor * (Math.Cos(theta) - Math.Sin(theta));
        double right = factor * (Math.Cos(theta) + Math.Sin(theta));
        return (left, right);
    }

    private static Task WriteStereoWavAsync(
        string path,
        int sampleRate,
        float[] interleavedStereo,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            int bitsPerSample = 16;
            int channels = 2;
            int blockAlign = channels * (bitsPerSample / 8);
            int byteRate = sampleRate * blockAlign;
            int dataLength = interleavedStereo.Length * 2;

            using FileStream stream = File.Create(path);
            using BinaryWriter writer = new(stream);

            writer.Write("RIFF"u8.ToArray());
            writer.Write(36 + dataLength);
            writer.Write("WAVE"u8.ToArray());

            writer.Write("fmt "u8.ToArray());
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write((short)blockAlign);
            writer.Write((short)bitsPerSample);

            writer.Write("data"u8.ToArray());
            writer.Write(dataLength);

            foreach (float sample in interleavedStereo)
            {
                cancellationToken.ThrowIfCancellationRequested();
                short pcm = (short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue);
                writer.Write(pcm);
            }
        }, cancellationToken);
    }

    /// <summary>Reads the OGG data offset from the 8-byte MOGG header.</summary>
    // HMX private key for MOGG version 0x0B (Rock Band 1 / RB1 DLC).
    // This is a well-known community-documented constant, identical across all 0x0B-encrypted MOGG files.
    private static readonly byte[] HmxPrivateKey0B =
    [
        0x37, 0xB2, 0xE2, 0xB9, 0x1C, 0x74, 0xFA, 0x9E,
        0x38, 0x81, 0x08, 0xEA, 0x36, 0x23, 0xDB, 0xE4,
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

    // C3 Custom Creators Collective private key for MOGG version 0x0D.
    // This is the community-documented symmetric key used by C3 Tools to encrypt custom exports.
    // See: https://github.com/trojannemo/Nautilus (Mogg.cs – C3_PRIVATE_KEY_D)
    private static readonly byte[] C3PrivateKey0D =
    [
        0xC0, 0x87, 0x69, 0x00, 0xE2, 0x7C, 0x73, 0xEB,
            0xCC, 0xD4, 0x21, 0x3D, 0x70, 0x2A, 0x4F, 0xED,
        ];

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
    ///   [20+N*8..20+N*8+71] PUBLIC_KEY (72 bytes; first 16 are the AES counter seed)
    ///   [OGG data offset..] OGG data (encrypted with AES-CTR using C3PrivateKey0D)
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

        int numPairs = moggBytes[16] | (moggBytes[17] << 8) | (moggBytes[18] << 16) | (moggBytes[19] << 24);
        int publicKeyOffset = 20 + (numPairs * 8);

        // PUBLIC_KEY for 0x0D is 72 bytes; first 16 bytes are the AES counter seed.
        if (moggBytes.Length < publicKeyOffset + 72)
        {
            throw new InvalidDataException(
                "0x0D MOGG file is too short to contain the 72-byte PUBLIC_KEY after the seek table.");
        }

        byte[] publicKey = moggBytes[publicKeyOffset..(publicKeyOffset + 72)];
        int oggDataStart = moggBytes[4] | (moggBytes[5] << 8) | (moggBytes[6] << 16) | (moggBytes[7] << 24);
        byte[] encryptedOgg = moggBytes[oggDataStart..];

        byte[] decryptedOgg = DecryptMoggAesCtr(encryptedOgg, C3PrivateKey0D, publicKey[..16]);

        // C3-encrypted MOGGs may start with HMXA instead of OggS. Fix the header if so.
        if (decryptedOgg.Length >= 4
            && decryptedOgg[0] == 0x48 && decryptedOgg[1] == 0x4D
            && decryptedOgg[2] == 0x58 && decryptedOgg[3] == 0x41)
        {
            FixHmxaHeader(decryptedOgg, publicKey);
        }

        return decryptedOgg;
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
