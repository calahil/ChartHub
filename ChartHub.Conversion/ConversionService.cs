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
        Action<ConversionProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class ConversionService : IConversionService
{
    public ConversionService(ConversionOptions? options = null)
    {
        _ = options;
    }

    /// <inheritdoc />
    public async Task<ConversionResult> ConvertAsync(
        string sourcePath,
        string outputRoot,
        Action<ConversionProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension is ".con" or ".rb3con")
        {
            return await ConvertConAsync(sourcePath, outputRoot, progress, cancellationToken).ConfigureAwait(false);
        }

        if (extension is ".sng")
        {
            return await ConvertSngAsync(sourcePath, outputRoot, progress, cancellationToken).ConfigureAwait(false);
        }

        // Some Xbox STFS exports are extensionless (for example LIVE packages).
        // Fall back to container magic sniffing so these packages still convert.
        byte[] magic = new byte[4];
        await using (FileStream stream = File.OpenRead(sourcePath))
        {
            if (await stream.ReadAsync(magic.AsMemory(0, magic.Length), cancellationToken).ConfigureAwait(false) == magic.Length
                && IsStfsContainerMagic(magic))
            {
                return await ConvertConAsync(sourcePath, outputRoot, progress, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new NotSupportedException($"Source file type '{extension}' is not supported. Expected .con, .rb3con, .sng, or an extensionless STFS package.");

    }

    private static bool IsStfsContainerMagic(ReadOnlySpan<byte> magic)
    {
        return magic.SequenceEqual("CON "u8)
            || magic.SequenceEqual("LIVE"u8)
            || magic.SequenceEqual("PIRS"u8);
    }

    private static async Task<ConversionResult> ConvertSngAsync(
        string sourcePath,
        string outputRoot,
        Action<ConversionProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        ReportProgress(progress, ConversionProgressStages.ParseContainer, 91, "Reading SNG package");
        byte[] containerBytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        SngPackage package = SngPackageReader.Read(containerBytes);
        ReportProgress(progress, ConversionProgressStages.ParseDta, 91.5, "Extracting SNG metadata");
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
            ReportProgress(progress, ConversionProgressStages.ConvertMidi, 92.5, "Extracting chart data");
            IReadOnlyList<SngChartContent> charts = SngMidiExtractor.ExtractCloneHeroCharts(package, containerBytes);
            foreach (SngChartContent chart in charts)
            {
                await File.WriteAllBytesAsync(Path.Combine(songDir, chart.FileName), chart.Bytes, cancellationToken).ConfigureAwait(false);
            }

            ReportProgress(progress, ConversionProgressStages.DecodeMogg, 93.5, "Extracting SNG audio payloads");
            ReportProgress(progress, ConversionProgressStages.MixBacking, 93.8, "Preparing backing audio output");
            ReportProgress(progress, ConversionProgressStages.MixStems, 94.0, "Preparing optional stem outputs");
            await SngAudioExtractor.ExtractAsync(package, containerBytes, songDir, cancellationToken).ConfigureAwait(false);

            try
            {
                ReportProgress(progress, ConversionProgressStages.ExtractAlbumArt, 94.5, "Extracting album art");
                await SngAlbumArtExtractor.ExtractAsync(package, containerBytes, songDir, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException)
            {
                // Album art is optional for SNG installs.
            }

            ReportProgress(progress, ConversionProgressStages.WriteSongIni, 95.5, "Writing song.ini");
            await WriteSngSongIniAsync(package, containerBytes, sourcePath, songDir, metadata, cancellationToken).ConfigureAwait(false);
            ReportProgress(progress, ConversionProgressStages.Finalize, 96, "Conversion output finalized");

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
        Action<ConversionProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        ReportProgress(progress, ConversionProgressStages.ParseContainer, 91, "Opening RB3CON container");
        using var stfs = StfsReader.Open(sourcePath);

        // --- Locate and parse songs.dta ---
        byte[]? dtaBytes = FindDta(stfs);
        if (dtaBytes == null)
        {
            throw new InvalidDataException($"No songs.dta found inside '{sourcePath}'.");
        }

        ReportProgress(progress, ConversionProgressStages.ParseDta, 91.5, "Parsing songs.dta");
        DtaSongInfo songInfo = DtaParser.Parse(dtaBytes);

        // --- Prepare output directory ---
        string songDirName = SanitiseDirName(songInfo.ShortName);
        string songDir = Path.Combine(outputRoot, songDirName);
        Directory.CreateDirectory(songDir);

        try
        {
            // --- Extract and convert MIDI ---
            ReportProgress(progress, ConversionProgressStages.ConvertMidi, 92.5, "Converting MIDI");
            await ExtractMidiAsync(stfs, songInfo, songDir, cancellationToken).ConfigureAwait(false);

            // --- Extract and split MOGG audio ---
            ReportProgress(progress, ConversionProgressStages.DecodeMogg, 93.2, "Decrypting and decoding MOGG audio");
            ReportProgress(progress, ConversionProgressStages.MixBacking, 93.6, "Generating backing track");
            ReportProgress(progress, ConversionProgressStages.MixStems, 94.0, "Generating optional stems");
            AudioExtractionOutcome audioOutcome = await ExtractAudioAsync(stfs, songInfo, songDir, cancellationToken).ConfigureAwait(false);
            DtaSongInfo effectiveSongInfo = WithSongLengthFromAudio(songInfo, audioOutcome.BackingDurationMs);

            // --- Extract and convert album art ---
            ReportProgress(progress, ConversionProgressStages.ExtractAlbumArt, 94.5, "Extracting album art");
            ExtractAlbumArt(stfs, songDir, songInfo.SongFilePath);

            // --- Write song.ini ---
            ReportProgress(progress, ConversionProgressStages.WriteSongIni, 95.5, "Writing song.ini");
            string songIniContent = SongIniGenerator.Generate(effectiveSongInfo);
            await File.WriteAllTextAsync(
                Path.Combine(songDir, "song.ini"),
                songIniContent,
                System.Text.Encoding.UTF8,
                cancellationToken).ConfigureAwait(false);
            ReportProgress(progress, ConversionProgressStages.Finalize, 96, "Conversion output finalized");

            var statuses = new List<ConversionStatus>();
            if (audioOutcome.IsAudioIncomplete)
            {
                statuses.Add(new ConversionStatus(
                    ConversionStatusCodes.AudioIncomplete,
                    "Conversion produced only the backing audio file because no instrument stems could be mapped from the source package."));
            }

            var metadata = new ConversionMetadata(effectiveSongInfo.Artist, effectiveSongInfo.Title, effectiveSongInfo.Charter);
            return statuses.Count == 0
                ? new ConversionResult(songDir, metadata)
                : new ConversionResult(songDir, metadata, statuses);
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

    private async Task<AudioExtractionOutcome> ExtractAudioAsync(
        StfsReader stfs,
        DtaSongInfo songInfo,
        string songDir,
        CancellationToken cancellationToken)
    {
        var extractor = new MoggExtractor();
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
            (byte[] MoggBytes, double DurationSeconds)? selectedAttempt = null;
            foreach (bool forceConsecutive in new[] { false, true })
            {
                try
                {
                    byte[] moggBytes = stfs.ReadEntry(entry, forceConsecutive);
                    double durationSeconds = extractor.EstimateBackingDurationSeconds(moggBytes);
                    if (selectedAttempt is null || durationSeconds > selectedAttempt.Value.DurationSeconds)
                    {
                        selectedAttempt = (moggBytes, durationSeconds);
                    }
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

            if (selectedAttempt is null)
            {
                continue;
            }

            IReadOnlyDictionary<string, string> stems = await extractor
                .ExtractStemsAsync(selectedAttempt.Value.MoggBytes, songInfo, songDir, cancellationToken)
                .ConfigureAwait(false);

            bool hasInstrumentStem = stems.Keys
                .Any(key => !string.Equals(key, "song", StringComparison.OrdinalIgnoreCase));
            int? backingDurationMs = selectedAttempt.Value.DurationSeconds > 0
                ? (int)(selectedAttempt.Value.DurationSeconds * 1000)
                : null;
            return new AudioExtractionOutcome(
                IsAudioIncomplete: !hasInstrumentStem,
                BackingDurationMs: backingDurationMs);
        }

        throw new InvalidDataException(
            "No MOGG audio file found inside the CON package.",
            lastError);
    }

    private readonly record struct AudioExtractionOutcome(bool IsAudioIncomplete, int? BackingDurationMs);

    private static DtaSongInfo WithSongLengthFromAudio(DtaSongInfo songInfo, int? backingDurationMs)
    {
        if (backingDurationMs is null or <= 0)
        {
            return songInfo;
        }

        return new DtaSongInfo
        {
            ShortName = songInfo.ShortName,
            Title = songInfo.Title,
            Artist = songInfo.Artist,
            Charter = songInfo.Charter,
            Album = songInfo.Album,
            Genre = songInfo.Genre,
            Year = songInfo.Year,
            AlbumTrack = songInfo.AlbumTrack,
            SongLengthMs = backingDurationMs.Value,
            PreviewStartMs = songInfo.PreviewStartMs,
            PreviewEndMs = songInfo.PreviewEndMs,
            VocalParts = songInfo.VocalParts,
            Ranks = songInfo.Ranks,
            SongFilePath = songInfo.SongFilePath,
            TrackChannels = songInfo.TrackChannels,
            Pans = songInfo.Pans,
            Vols = songInfo.Vols,
            TotalChannels = songInfo.TotalChannels,
        };
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
        string? songSubfolder = null;
        if (!string.IsNullOrEmpty(songFilePath))
        {
            string normalized = songFilePath.Replace('\\', '/');
            int lastSlash = normalized.LastIndexOf('/');
            if (lastSlash > 0)
            {
                songSubfolder = normalized[..(lastSlash + 1)];
            }
        }

        bool TryWriteFromPath(string path, bool decodeXboxTexture)
        {
            byte[]? bytes = stfs.ReadFile(path);
            if (bytes == null)
            {
                return false;
            }

            try
            {
                if (decodeXboxTexture)
                {
                    byte[] pngBytes = PngXboxDecoder.Decode(bytes);
                    File.WriteAllBytes(Path.Combine(songDir, "album.png"), pngBytes);
                    return true;
                }

                string ext = Path.GetExtension(path).ToLowerInvariant();
                string outName = ext is ".jpg" or ".jpeg" ? "album.jpg" : "album.png";
                File.WriteAllBytes(Path.Combine(songDir, outName), bytes);
                return true;
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }

        IEnumerable<(string path, StfsEntry entry)> entries = stfs.GetAllFiles();

        // Pass 1: scoped to song subfolder when available.
        foreach ((string path, _) in entries)
        {
            string normPath = path.Replace('\\', '/');
            if (songSubfolder != null && !normPath.StartsWith(songSubfolder, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if ((normPath.EndsWith(".png_xbox", StringComparison.OrdinalIgnoreCase)
                || normPath.EndsWith("_keep.png_xbox", StringComparison.OrdinalIgnoreCase))
                && TryWriteFromPath(path, decodeXboxTexture: true))
            {
                return;
            }

            if ((normPath.EndsWith("album.png", StringComparison.OrdinalIgnoreCase)
                || normPath.EndsWith("album.jpg", StringComparison.OrdinalIgnoreCase)
                || normPath.EndsWith("album.jpeg", StringComparison.OrdinalIgnoreCase))
                && TryWriteFromPath(path, decodeXboxTexture: false))
            {
                return;
            }
        }

        // Pass 2: package-wide fallback.
        foreach ((string path, _) in entries)
        {
            string normPath = path.Replace('\\', '/');
            if ((normPath.EndsWith(".png_xbox", StringComparison.OrdinalIgnoreCase)
                || normPath.EndsWith("_keep.png_xbox", StringComparison.OrdinalIgnoreCase))
                && TryWriteFromPath(path, decodeXboxTexture: true))
            {
                return;
            }

            if ((normPath.EndsWith("album.png", StringComparison.OrdinalIgnoreCase)
                || normPath.EndsWith("album.jpg", StringComparison.OrdinalIgnoreCase)
                || normPath.EndsWith("album.jpeg", StringComparison.OrdinalIgnoreCase))
                && TryWriteFromPath(path, decodeXboxTexture: false))
            {
                return;
            }
        }
    }

    private static string SanitiseDirName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (char c in name)
        {
            // Keep a conservative subset so downstream tooling never receives
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

    private static void ReportProgress(
        Action<ConversionProgressUpdate>? progress,
        string stage,
        double percent,
        string? message = null)
    {
        progress?.Invoke(new ConversionProgressUpdate(stage, percent, message));
    }
}
