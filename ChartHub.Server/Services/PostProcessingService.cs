using ChartHub.Conversion.Midi;
using ChartHub.Server.Contracts;
using ChartHub.Server.Options;

using Microsoft.Extensions.Options;

namespace ChartHub.Server.Services;

public interface IPostProcessingService
{
    /// <summary>
    /// Merges the generated drum MIDI into the song's installed folder.
    /// The original folder is archived to <see cref="ServerPathOptions.CloneHeroArchiveRoot"/> first.
    /// </summary>
    void ApplyApprovedDrumResult(string songId, string midiResultPath);
}

public sealed class PostProcessingService : IPostProcessingService
{
    private readonly ServerPathOptions _pathOptions;
    private readonly ICloneHeroLibraryService _library;
    private readonly IDrumMidiMerger _drumMidiMerger;
    private readonly ILogger<PostProcessingService> _logger;

    public PostProcessingService(
        IOptions<ServerPathOptions> pathOptions,
        ICloneHeroLibraryService library,
        IDrumMidiMerger drumMidiMerger,
        ILogger<PostProcessingService> logger)
    {
        _pathOptions = pathOptions.Value;
        _library = library;
        _drumMidiMerger = drumMidiMerger;
        _logger = logger;
    }

    public void ApplyApprovedDrumResult(string songId, string midiResultPath)
    {
        if (!_library.TryGetSong(songId, out CloneHeroSongResponse? song) || song?.InstalledPath is null)
        {
            PostProcessLog.SongNotFound(_logger, songId);
            return;
        }

        string installedPath = song.InstalledPath;

        // Archive the original song folder before modifying it.
        string archiveRoot = _pathOptions.CloneHeroArchiveRoot;
        string archiveDest = Path.Combine(archiveRoot, songId);
        if (!Directory.Exists(archiveDest))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(archiveDest)!);
            CopyDirectory(installedPath, archiveDest);
            PostProcessLog.SongArchived(_logger, songId, archiveDest);
        }

        string existingMidiPath = Path.Combine(installedPath, "notes.mid");
        string existingChartPath = Path.Combine(installedPath, "notes.chart");

        byte[] mergedMidi;
        if (File.Exists(existingMidiPath))
        {
            byte[] existingMidi = File.ReadAllBytes(existingMidiPath);
            byte[] generatedMidi = File.ReadAllBytes(midiResultPath);
            mergedMidi = _drumMidiMerger.MergeGeneratedDrums(existingMidi, generatedMidi);
        }
        else
        {
            // If this install originated from notes.chart, preserve notes.chart and promote generated MIDI to notes.mid.
            if (File.Exists(existingChartPath))
            {
                PostProcessLog.ChartOnlyPromotedToMidi(_logger, songId, existingChartPath, existingMidiPath);
            }

            // No existing notes.mid to merge into.
            mergedMidi = File.ReadAllBytes(midiResultPath);
        }

        File.WriteAllBytes(existingMidiPath, mergedMidi);
        PostProcessLog.DrumsMerged(_logger, songId, existingMidiPath);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (string subDir in Directory.GetDirectories(source))
        {
            CopyDirectory(subDir, Path.Combine(destination, Path.GetFileName(subDir)));
        }
    }
}

internal static partial class PostProcessLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "PostProcessingService: song {SongId} not found or not installed.")]
    public static partial void SongNotFound(ILogger logger, string songId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Song {SongId} archived to {ArchivePath}.")]
    public static partial void SongArchived(ILogger logger, string songId, string archivePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "notes.chart-only song {SongId} detected at {ChartPath}; generated drums promoted to {MidiPath}.")]
    public static partial void ChartOnlyPromotedToMidi(ILogger logger, string songId, string chartPath, string midiPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Drum MIDI merged for song {SongId} → {MidiPath}.")]
    public static partial void DrumsMerged(ILogger logger, string songId, string midiPath);
}
