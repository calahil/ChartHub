using System.Diagnostics;
using System.Security.Cryptography;

using ChartHub.Conversion.Dta;

namespace ChartHub.Conversion.Audio;

/// <summary>
/// Extracts OGG Vorbis stems from a MOGG file using ffmpeg.
/// </summary>
/// <remarks>
/// A MOGG file is an 8-byte header (<c>0A 00 00 00 &lt;offset-LE32&gt;</c>) followed by a
/// standard OGG Vorbis multistream. <paramref name="ffmpegPath"/> must point to an ffmpeg
/// executable with OGG/Vorbis decode support.
/// </remarks>
internal sealed class MoggExtractor
{
    private readonly string _ffmpegPath;

    public MoggExtractor(string? ffmpegPath = null)
    {
        _ffmpegPath = ffmpegPath ?? "ffmpeg";
    }

    /// <summary>
    /// Splits a MOGG file into per-stem OGG files written to <paramref name="outputDir"/>.
    /// </summary>
    /// <param name="moggBytes">Raw MOGG file bytes.</param>
    /// <param name="songInfo">DTA metadata providing channel-to-stem mapping.</param>
    /// <param name="outputDir">Directory where stem OGGs will be written.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Map of stem name (e.g. "guitar", "drums") to output OGG file path.</returns>
    public async Task<IReadOnlyDictionary<string, string>> ExtractStemsAsync(
        byte[] moggBytes,
        DtaSongInfo songInfo,
        string outputDir,
        CancellationToken cancellationToken = default)
    {
        // Write MOGG data to a temp file, decrypting if needed to recover raw OGG bytes.
        string tempOgg = Path.Combine(outputDir, "__mogg_raw.ogg");
        byte[] rawOggBytes = ExtractRawOggBytes(moggBytes);
        await File.WriteAllBytesAsync(tempOgg, rawOggBytes, cancellationToken)
            .ConfigureAwait(false);

        int? vorbisChannelCount = TryReadVorbisChannelCount(rawOggBytes);

        var stems = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // Produce a backing (mix-down) track from all channels as a fallback.
            string backingPath = Path.Combine(outputDir, "song.ogg");
            await RunFfmpegAsync(
                ["-y", "-i", tempOgg, "-c:a", "libvorbis", "-q:a", "6", backingPath],
                cancellationToken).ConfigureAwait(false);
            stems["song"] = backingPath;

            // Extract per-stem OGG files using channel filter expressions.
            foreach ((string stem, IReadOnlyList<int> channels) in songInfo.TrackChannels)
            {
                if (channels.Count == 0)
                {
                    continue;
                }

                // Some fan-made packages have DTA channel mappings that exceed the actual
                // Vorbis stream channel count. Skip those stems and keep conversion alive
                // using the full backing track.
                if (vorbisChannelCount.HasValue && channels.Any(channel => channel < 0 || channel >= vorbisChannelCount.Value))
                {
                    continue;
                }

                string stemFile = Path.Combine(outputDir, $"{NormaliseStemName(stem)}.ogg");
                string audioFilter = BuildChannelFilter(channels, songInfo.TotalChannels);
                await RunFfmpegAsync(
                    ["-y", "-i", tempOgg, "-af", audioFilter,
                     "-c:a", "libvorbis", "-q:a", "6", stemFile],
                    cancellationToken).ConfigureAwait(false);
                stems[stem] = stemFile;
            }
        }
        finally
        {
            TryDeleteFile(tempOgg);
        }

        return stems;
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
            _ => throw new NotSupportedException(
                $"MOGG encryption version 0x{version:X2} is not supported. " +
                "Only unencrypted (0x0A) and RB1-style encrypted (0x0B) MOGG files are handled."),
        };
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

    /// <summary>
    /// Builds an ffmpeg filter_complex string that maps specific OGG channels to a stereo output.
    /// </summary>
    private static string BuildChannelFilter(IReadOnlyList<int> channels, int totalChannels)
    {
        // OGG Vorbis stores multi-channel audio as a single stream.
        // Use pan filter to extract the relevant channels into stereo.
        _ = totalChannels; // used for future validation

        if (channels.Count == 1)
        {
            // Mono channel duplicated to stereo
            return $"pan=stereo|c0=c{channels[0]}|c1=c{channels[0]}";
        }

        if (channels.Count >= 2)
        {
            // Use first two channels as L/R
            return $"pan=stereo|c0=c{channels[0]}|c1=c{channels[1]}";
        }

        return "aformat=channel_layouts=stereo";
    }

    private static string NormaliseStemName(string stem)
    {
        return stem.ToLowerInvariant() switch
        {
            "drum" or "drums" => "drums",
            "bass" => "bass",
            "guitar" => "guitar",
            "vocals" or "vocal" => "vocals",
            "keys" => "keys",
            "crowd" => "crowd",
            _ => stem.ToLowerInvariant(),
        };
    }

    private static int? TryReadVorbisChannelCount(ReadOnlySpan<byte> oggBytes)
    {
        ReadOnlySpan<byte> marker = [
            (byte)0x01,
            (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s',
        ];

        for (int i = 0; i <= oggBytes.Length - marker.Length - 5; i++)
        {
            if (!oggBytes.Slice(i, marker.Length).SequenceEqual(marker))
            {
                continue;
            }

            int channelOffset = i + marker.Length + 4;
            if (channelOffset >= oggBytes.Length)
            {
                return null;
            }

            int channels = oggBytes[channelOffset];
            return channels > 0 ? channels : null;
        }

        return null;
    }

    private async Task RunFfmpegAsync(string[] args, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        foreach (string arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start ffmpeg.");

        using CancellationTokenRegistration reg = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch { /* best-effort */ }
        });

        string stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            string stderrTail = stderr.Length <= 2000 ? stderr : stderr[^2000..];
            throw new InvalidOperationException(
                $"ffmpeg exited with code {process.ExitCode}. Command: {_ffmpegPath} {string.Join(' ', args)}\n{stderrTail}");
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch { /* best-effort */ }
    }
}
