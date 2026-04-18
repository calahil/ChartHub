using System.Diagnostics;

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
        // Write MOGG data to a temp file (stripping the 8-byte MOGG header to get raw OGG).
        string tempOgg = Path.Combine(outputDir, "__mogg_raw.ogg");
        int oggOffset = ReadMoggOggOffset(moggBytes);
        await File.WriteAllBytesAsync(tempOgg, moggBytes[oggOffset..], cancellationToken)
            .ConfigureAwait(false);

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

                string stemFile = Path.Combine(outputDir, $"{NormaliseStemName(stem)}.ogg");
                string filterGraph = BuildChannelFilter(channels, songInfo.TotalChannels);
                await RunFfmpegAsync(
                    ["-y", "-i", tempOgg, "-filter_complex", filterGraph,
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
    private static int ReadMoggOggOffset(byte[] bytes)
    {
        if (bytes.Length < 8)
        {
            throw new InvalidDataException("MOGG file is too short to contain a valid header.");
        }

        // Bytes [4–7]: OGG start offset, little-endian 32-bit.
        int offset = bytes[4] | (bytes[5] << 8) | (bytes[6] << 16) | (bytes[7] << 24);
        if (offset < 8 || offset >= bytes.Length)
        {
            // Fallback: RB2 MOGGs use version 0x0A with offset always 8
            return 8;
        }

        return offset;
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
            return $"[0:a]pan=stereo|c0=c{channels[0]}|c1=c{channels[0]}[out];[out]";
        }

        if (channels.Count >= 2)
        {
            // Use first two channels as L/R
            return $"[0:a]pan=stereo|c0=c{channels[0]}|c1=c{channels[1]}[out];[out]";
        }

        return "[0:a]aformat=channel_layouts=stereo[out];[out]";
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
            throw new InvalidOperationException(
                $"ffmpeg exited with code {process.ExitCode}: {stderr.AsSpan(0, Math.Min(400, stderr.Length))}");
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch { /* best-effort */ }
    }
}
