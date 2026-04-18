using ChartHub.Conversion.Audio;
using ChartHub.Conversion.Dta;
using ChartHub.Conversion.Image;
using ChartHub.Conversion.Midi;
using ChartHub.Conversion.Models;
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
            _ => throw new NotSupportedException($"Source file type '{extension}' is not supported. Expected .con or .rb3con."),
        };
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
            ExtractAlbumArt(stfs, songDir);

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
        byte[]? midiBytes = null;

        // Prefer MIDI named after the song short name
        foreach ((string path, _) in stfs.GetAllFiles())
        {
            string name = Path.GetFileName(path);
            if (name.EndsWith(".mid", StringComparison.OrdinalIgnoreCase))
            {
                midiBytes = stfs.ReadFile(path);
                break;
            }
        }

        if (midiBytes == null)
        {
            throw new InvalidDataException("No MIDI track found inside the CON package.");
        }

        byte[] chMidi = RbMidiConverter.Convert(midiBytes);
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
        byte[]? moggBytes = null;

        foreach ((string path, _) in stfs.GetAllFiles())
        {
            if (path.EndsWith(".mogg", StringComparison.OrdinalIgnoreCase))
            {
                moggBytes = stfs.ReadFile(path);
                break;
            }
        }

        if (moggBytes == null)
        {
            throw new InvalidDataException("No MOGG audio file found inside the CON package.");
        }

        var extractor = new MoggExtractor(_options.FfmpegPath);
        await extractor.ExtractStemsAsync(moggBytes, songInfo, songDir, cancellationToken)
            .ConfigureAwait(false);
    }

    private static void ExtractAlbumArt(StfsReader stfs, string songDir)
    {
        foreach ((string path, _) in stfs.GetAllFiles())
        {
            if (path.EndsWith(".png_xbox", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("_keep.png_xbox", StringComparison.OrdinalIgnoreCase))
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
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        return sb.Length > 0 ? sb.ToString() : "song";
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
