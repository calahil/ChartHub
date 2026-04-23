using ChartHub.Conversion.Audio;
using ChartHub.Conversion.Dta;
using ChartHub.Conversion.Image;
using ChartHub.Conversion.Midi;
using ChartHub.Conversion.Models;
using ChartHub.Conversion.Sng;
using ChartHub.Conversion.SongIni;
using ChartHub.Conversion.Stfs;

namespace ChartHub.Conversion;

/// <summary>Converts a Rock Band CON package into a Clone Hero song folder.</summary>
public interface IConversionService
{
    /// <summary>
    /// Converts the source CON/SNG file at <paramref name="sourcePath"/> into a Clone Hero
    /// song folder written inside <paramref name="outputRoot"/>.
    /// </summary>
    Task<ConversionResult> ConvertAsync(
        string sourcePath,
        string outputRoot,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class ConversionService : IConversionService
{
    private readonly ConversionOptions _options;

    public ConversionService(ConversionOptions? options = null)
    {
        _options = options ?? new ConversionOptions();
    }

    /// <inheritdoc />
    public async Task<ConversionResult> ConvertAsync(
        string sourcePath,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        string extension = Path.GetExtension(sourcePath).ToLowerInvariant();

        return extension switch
        {
            ".con" or ".rb3con" => await ConvertConAsync(sourcePath, outputRoot, cancellationToken).ConfigureAwait(false),
            ".sng" => await ConvertSngAsync(sourcePath, outputRoot, cancellationToken).ConfigureAwait(false),
            _ => throw new NotSupportedException($"Source file type '{extension}' is not supported. Expected .con, .rb3con, or .sng."),
        };
    }

    private static async Task<ConversionResult> ConvertSngAsync(
        string sourcePath,
        string outputRoot,
        CancellationToken cancellationToken)
    {
        byte[] containerBytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        SngPackage package = SngPackageReader.Read(containerBytes);
        ConversionMetadata metadata = SngMetadataExtractor.Extract(package, containerBytes, sourcePath);

        string songDirName = SanitiseDirName(metadata.Title);
        if (string.IsNullOrWhiteSpace(songDirName))
        {
            songDirName = SanitiseDirName(Path.GetFileNameWithoutExtension(sourcePath));
        }

        if (string.IsNullOrWhiteSpace(songDirName))
        {
            songDirName = "Unknown Song";
        }

        string songDir = Path.Combine(outputRoot, songDirName);
        Directory.CreateDirectory(songDir);

        try
        {
            byte[] midiBytes = SngMidiExtractor.ExtractCloneHeroMidi(package, containerBytes);
            await File.WriteAllBytesAsync(Path.Combine(songDir, "notes.mid"), midiBytes, cancellationToken).ConfigureAwait(false);

            await SngAudioExtractor.ExtractAsync(package, containerBytes, songDir, cancellationToken).ConfigureAwait(false);

            try
            {
                await SngAlbumArtExtractor.ExtractAsync(package, containerBytes, songDir, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException)
            {
                // Album art is optional for SNG installs.
            }

            await WriteSngSongIniAsync(package, containerBytes, sourcePath, songDir, metadata, cancellationToken).ConfigureAwait(false);

            return new ConversionResult(songDir, metadata);
        }
        catch
        {
            TryDeleteDirectory(songDir);
            throw;
        }
    }

    private async Task<ConversionResult> ConvertConAsync(
        string sourcePath,
        string outputRoot,
        CancellationToken cancellationToken)
    {
        using var stfs = StfsReader.Open(sourcePath);

        // --- Locate and parse songs.dta ---
        byte[]? dtaBytes = FindDta(stfs);
        if (dtaBytes == null)
        {
            throw new InvalidDataException($"No songs.dta found inside '{sourcePath}'.");
        }

        DtaSongInfo songInfo = DtaParser.Parse(dtaBytes);

        // --- Prepare output directory ---
        string songDirName = SanitiseDirName(songInfo.ShortName);
        string songDir = Path.Combine(outputRoot, songDirName);
        Directory.CreateDirectory(songDir);

        try
        {
            // --- Extract and convert MIDI ---
            await ExtractMidiAsync(stfs, songInfo, songDir, cancellationToken).ConfigureAwait(false);

            // --- Extract and split MOGG audio ---
            await ExtractAudioAsync(stfs, songInfo, songDir, cancellationToken).ConfigureAwait(false);

            // --- Extract and convert album art ---
            ExtractAlbumArt(stfs, songDir, songInfo.SongFilePath);

            // --- Write song.ini ---
            string songIniContent = SongIniGenerator.Generate(songInfo);
            await File.WriteAllTextAsync(
                Path.Combine(songDir, "song.ini"),
                songIniContent,
                System.Text.Encoding.UTF8,
                cancellationToken).ConfigureAwait(false);

            var metadata = new ConversionMetadata(songInfo.Artist, songInfo.Title, songInfo.Charter);
            return new ConversionResult(songDir, metadata);
        }
        catch
        {
            // Clean up partial output so callers see a clean working dir on failure.
            TryDeleteDirectory(songDir);
            throw;
        }
    }

    // -------------------------------------------------------------------------

    private static byte[]? FindDta(StfsReader stfs)
    {
        foreach ((string path, _) in stfs.GetAllFiles())
        {
            if (path.EndsWith("songs.dta", StringComparison.OrdinalIgnoreCase))
            {
                return stfs.ReadFile(path);
            }
        }

        return null;
    }

    private static async Task ExtractMidiAsync(
        StfsReader stfs,
        DtaSongInfo songInfo,
        string songDir,
        CancellationToken cancellationToken)
    {
        StfsEntry? midiEntry = null;

        // Prefer the MIDI whose path matches the song's declared SongFilePath (multi-song packs
        // have one .mid per song; taking the first file would pick the wrong song).
        if (!string.IsNullOrEmpty(songInfo.SongFilePath))
        {
            string expectedSuffix = (songInfo.SongFilePath + ".mid").Replace('\\', '/');
            foreach ((string path, StfsEntry entry) in stfs.GetAllFiles())
            {
                string normPath = path.Replace('\\', '/');
                if (normPath.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    midiEntry = entry;
                    break;
                }
            }
        }

        // Fallback: first .mid in the package.
        if (midiEntry == null)
        {
            foreach ((string path, StfsEntry entry) in stfs.GetAllFiles())
            {
                if (path.EndsWith(".mid", StringComparison.OrdinalIgnoreCase))
                {
                    midiEntry = entry;
                    break;
                }
            }
        }

        if (midiEntry == null)
        {
            throw new InvalidDataException("No MIDI track found inside the CON package.");
        }

        byte[]? midiBytes = stfs.ReadEntry(midiEntry);

        // Fan-made multi-song packs can have broken hash-chain pointers that cause the block
        // reader to return blocks out of order. If the read doesn't start with the MThd magic,
        // retry with forced sequential traversal.
        if (midiBytes != null && !StartsWithMidiHeader(midiBytes) && !midiEntry.IsConsecutive)
        {
            midiBytes = stfs.ReadEntry(midiEntry, forceConsecutive: true);
        }

        if (midiBytes == null)
        {
            throw new InvalidDataException("No MIDI track found inside the CON package.");
        }

        byte[] chMidi;
        try
        {
            chMidi = RbMidiConverter.Convert(midiBytes);
        }
        catch (InvalidDataException)
        {
            // Block chain traversal may have assembled blocks in the wrong order even though
            // the first block started with a valid MThd header. Retry with consecutive traversal.
            midiBytes = stfs.ReadEntry(midiEntry, forceConsecutive: true);
            chMidi = RbMidiConverter.Convert(midiBytes);
        }
        await File.WriteAllBytesAsync(
            Path.Combine(songDir, "notes.mid"),
            chMidi,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ExtractAudioAsync(
        StfsReader stfs,
        DtaSongInfo songInfo,
        string songDir,
        CancellationToken cancellationToken)
    {
        var extractor = new MoggExtractor(_options.FfmpegPath);
        var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<(string Path, StfsEntry Entry)>();

        // Prefer the MOGG whose path matches the song's declared SongFilePath.
        if (!string.IsNullOrEmpty(songInfo.SongFilePath))
        {
            string expectedSuffix = (songInfo.SongFilePath + ".mogg").Replace('\\', '/');
            foreach ((string path, StfsEntry entry) in stfs.GetAllFiles())
            {
                string normPath = path.Replace('\\', '/');
                if (normPath.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase)
                    && tried.Add(normPath))
                {
                    candidates.Add((path, entry));
                }
            }
        }

        // Fallback: any .mogg in the package.
        foreach ((string path, StfsEntry entry) in stfs.GetAllFiles())
        {
            string normPath = path.Replace('\\', '/');
            if (normPath.EndsWith(".mogg", StringComparison.OrdinalIgnoreCase)
                && tried.Add(normPath))
            {
                candidates.Add((path, entry));
            }
        }

        Exception? lastError = null;
        foreach ((string path, StfsEntry entry) in candidates)
        {
            foreach (bool forceConsecutive in new[] { false, true })
            {
                try
                {
                    byte[] moggBytes = stfs.ReadEntry(entry, forceConsecutive);
                    await extractor.ExtractStemsAsync(moggBytes, songInfo, songDir, cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }
                catch (InvalidDataException ex)
                {
                    lastError = ex;
                }
                catch (NotSupportedException ex)
                {
                    lastError = ex;
                }
            }
        }

        throw new InvalidDataException(
            "No MOGG audio file found inside the CON package.",
            lastError);
    }

    private static bool StartsWithMidiHeader(ReadOnlySpan<byte> bytes)
    {
        // Standard MIDI files begin with the MThd chunk header: 'M','T','h','d'
        return bytes.Length >= 4
            && bytes[0] == (byte)'M'
            && bytes[1] == (byte)'T'
            && bytes[2] == (byte)'h'
            && bytes[3] == (byte)'d';
    }

    private static async Task WriteSngSongIniAsync(
        SngPackage package,
        byte[] containerBytes,
        string sourcePath,
        string songDir,
        ConversionMetadata metadata,
        CancellationToken cancellationToken)
    {
        string songIniPath = Path.Combine(songDir, "song.ini");

        if (SngPackageReader.TryFindEntry(package, "song.ini", out SngFileEntry? songIniEntry)
            && songIniEntry != null)
        {
            byte[] songIniBytes = SngPackageReader.ReadFileData(containerBytes, songIniEntry);
            await File.WriteAllBytesAsync(songIniPath, songIniBytes, cancellationToken).ConfigureAwait(false);
            return;
        }

        string shortName = Path.GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(shortName))
        {
            shortName = metadata.Title;
        }

        string songIniContent = SongIniGenerator.Generate(new DtaSongInfo
        {
            ShortName = shortName,
            Title = metadata.Title,
            Artist = metadata.Artist,
            Charter = metadata.Charter,
        });

        await File.WriteAllTextAsync(songIniPath, songIniContent, System.Text.Encoding.UTF8, cancellationToken).ConfigureAwait(false);
    }

    private static void ExtractAlbumArt(StfsReader stfs, string songDir, string? songFilePath = null)
    {
        // Determine the song subfolder to prefer art from the correct song in multi-song packs.
        // SongFilePath is like "songs/<shortname>/<shortname>"; the parent dir is "songs/<shortname>/".
        string? songSubfolder = null;
        if (!string.IsNullOrEmpty(songFilePath))
        {
            string normalized = songFilePath.Replace('\\', '/');
            int lastSlash = normalized.LastIndexOf('/');
            if (lastSlash > 0)
            {
                songSubfolder = normalized[..(lastSlash + 1)]; // "songs/<shortname>/"
            }
        }

        foreach ((string path, _) in stfs.GetAllFiles())
        {
            string normPath = path.Replace('\\', '/');
            if (songSubfolder != null && !normPath.StartsWith(songSubfolder, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (normPath.EndsWith(".png_xbox", StringComparison.OrdinalIgnoreCase)
                || normPath.EndsWith("_keep.png_xbox", StringComparison.OrdinalIgnoreCase))
            {
                byte[]? texBytes = stfs.ReadFile(path);
                if (texBytes == null)
                {
                    continue;
                }

                try
                {
                    byte[] pngBytes = PngXboxDecoder.Decode(texBytes);
                    File.WriteAllBytes(Path.Combine(songDir, "album.png"), pngBytes);
                    return; // use first valid image found
                }
                catch (InvalidDataException)
                {
                    // Non-fatal: album art is optional.
                }

                break;
            }
        }
    }

    private static string SanitiseDirName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (char c in name)
        {
            // Keep a conservative subset so downstream tools (ffmpeg, etc.) never receive
            // path segments with URL/framing characters like '#', '?', or '&'.
            bool isSafe = char.IsLetterOrDigit(c)
                || c == ' '
                || c == '-'
                || c == '_'
                || c == '.'
                || c == '(' || c == ')';

            if (!isSafe || Array.IndexOf(invalid, c) >= 0 || char.IsControl(c))
            {
                sb.Append('_');
            }
            else
            {
                sb.Append(c);
            }
        }

        string sanitized = sb.ToString().Trim(' ', '.');
        return sanitized.Length > 0 ? sanitized : "song";
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try { Directory.Delete(path, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
