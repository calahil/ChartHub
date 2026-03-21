using ChartHub.Utilities;

namespace ChartHub.Services;

public sealed record CloneHeroReconciliationProgress(
    string Message,
    int ProcessedSongs,
    int TotalSongs,
    double? ProgressPercent);

public sealed record CloneHeroReconciliationResult(
    int Scanned,
    int Updated,
    int Renamed,
    int Failed);

public interface ICloneHeroLibraryReconciliationService
{
    Task<CloneHeroReconciliationResult> ReconcileAsync(
        IProgress<CloneHeroReconciliationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<bool> ReconcileSongDirectoryAsync(string songDirectory, CancellationToken cancellationToken = default);

    Task<bool> ReParseMetadataAsync(string songDirectory, CancellationToken cancellationToken = default);
}

public sealed class CloneHeroLibraryReconciliationService(
    AppGlobalSettings settings,
    LibraryCatalogService libraryCatalog,
    ISongIniMetadataParser songIniMetadataParser,
    ICloneHeroDirectorySchemaService schemaService) : ICloneHeroLibraryReconciliationService
{
    private readonly AppGlobalSettings _settings = settings;
    private readonly LibraryCatalogService _libraryCatalog = libraryCatalog;
    private readonly ISongIniMetadataParser _songIniMetadataParser = songIniMetadataParser;
    private readonly ICloneHeroDirectorySchemaService _schemaService = schemaService;

    public async Task<CloneHeroReconciliationResult> ReconcileAsync(
        IProgress<CloneHeroReconciliationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_settings.CloneHeroSongsDir);

        await _libraryCatalog.RemoveDuplicateLocalPathEntriesUnderRootAsync(
            _settings.CloneHeroSongsDir,
            cancellationToken).ConfigureAwait(false);

        var songIniFiles = Directory
            .EnumerateFiles(_settings.CloneHeroSongsDir, "song.ini", SearchOption.AllDirectories)
            .ToList();
        var total = songIniFiles.Count;

        var scanned = 0;
        var updated = 0;
        var renamed = 0;
        var failed = 0;

        foreach (var songIniPath in songIniFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var songDirectory = Path.GetDirectoryName(songIniPath);
            if (string.IsNullOrWhiteSpace(songDirectory))
                continue;

            scanned++;
            progress?.Report(new CloneHeroReconciliationProgress(
                Message: $"Reconciling {Path.GetFileName(songDirectory)}",
                ProcessedSongs: scanned,
                TotalSongs: total,
                ProgressPercent: total == 0 ? 100 : scanned * 100d / total));

            try
            {
                var result = await ReconcileSongDirectoryInternalAsync(songDirectory, cancellationToken).ConfigureAwait(false);
                if (!result.Updated)
                    continue;

                updated++;
                if (result.Renamed)
                    renamed++;
            }
            catch (Exception ex)
            {
                failed++;
                Logger.LogError("CloneHero", "Failed to reconcile song directory", ex, new Dictionary<string, object?>
                {
                    ["songDirectory"] = songDirectory,
                });
            }
        }

        // Clean stale catalog rows that reference paths that no longer exist after moves.
        await _libraryCatalog.RemoveMissingLocalFilesAsync(cancellationToken).ConfigureAwait(false);

        progress?.Report(new CloneHeroReconciliationProgress(
            Message: $"Reconciliation complete. Updated {updated} song(s).",
            ProcessedSongs: scanned,
            TotalSongs: total,
            ProgressPercent: 100));

        return new CloneHeroReconciliationResult(Scanned: scanned, Updated: updated, Renamed: renamed, Failed: failed);
    }

    public async Task<bool> ReconcileSongDirectoryAsync(string songDirectory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(songDirectory) || !Directory.Exists(songDirectory))
            return false;

        var songIniPath = Path.Combine(songDirectory, "song.ini");
        if (!File.Exists(songIniPath))
            return false;

        var result = await ReconcileSongDirectoryInternalAsync(songDirectory, cancellationToken).ConfigureAwait(false);
        return result.Updated;
    }

    public async Task<bool> ReParseMetadataAsync(string songDirectory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(songDirectory) || !Directory.Exists(songDirectory))
            return false;

        var songIniPath = Path.Combine(songDirectory, "song.ini");
        if (!File.Exists(songIniPath))
            return false;

        var metadata = _songIniMetadataParser.ParseFromSongIni(songIniPath);
        var existingEntry = await _libraryCatalog.GetEntryByLocalPathAsync(songDirectory, cancellationToken).ConfigureAwait(false);
        var normalizedSource = _schemaService.NormalizeSource(existingEntry?.Source);
        var persistedSourceId = existingEntry?.SourceId ?? string.Empty;

        await _libraryCatalog.UpsertAsync(new LibraryCatalogEntry(
            Source: normalizedSource,
            SourceId: persistedSourceId,
            Title: metadata.Title,
            Artist: metadata.Artist,
            Charter: metadata.Charter,
            LocalPath: songDirectory,
            AddedAtUtc: existingEntry?.AddedAtUtc ?? DateTimeOffset.UtcNow,
            ExternalKeyHash: existingEntry?.ExternalKeyHash,
            InternalIdentityKey: existingEntry?.InternalIdentityKey,
            ContentIdentityHash: existingEntry?.ContentIdentityHash), cancellationToken).ConfigureAwait(false);

        return true;
    }

    private async Task<(bool Updated, bool Renamed)> ReconcileSongDirectoryInternalAsync(string songDirectory, CancellationToken cancellationToken)
    {
        var songIniPath = Path.Combine(songDirectory, "song.ini");
        if (!File.Exists(songIniPath))
            return (false, false);

        var metadata = _songIniMetadataParser.ParseFromSongIni(songIniPath);
        var existingEntry = await _libraryCatalog.GetEntryByLocalPathAsync(songDirectory, cancellationToken).ConfigureAwait(false);
        var normalizedSource = _schemaService.NormalizeSource(existingEntry?.Source);

        var layout = _schemaService.ResolveUniqueLayout(
            _settings.CloneHeroSongsDir,
            metadata,
            normalizedSource,
            exists: path => string.Equals(path, songDirectory, StringComparison.Ordinal) || Directory.Exists(path));

        var finalPath = layout.FullPath;
        var renamed = false;
        if (!string.Equals(songDirectory, finalPath, StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
            Directory.Move(songDirectory, finalPath);
            renamed = true;
        }

        var contentIdentityHash = await LibraryIdentityService.ComputeInstalledContentIdentityHashAsync(finalPath, cancellationToken).ConfigureAwait(false);
        var internalIdentityKey = LibraryIdentityService.BuildInternalIdentityKey(contentIdentityHash, metadata);
        var persistedSourceId = string.IsNullOrWhiteSpace(existingEntry?.SourceId)
            ? internalIdentityKey
            : LibraryIdentityService.NormalizeSourceKey(normalizedSource, existingEntry!.SourceId);

        await _libraryCatalog.RemoveOtherEntriesByLocalPathAsync(
            finalPath,
            normalizedSource,
            persistedSourceId,
            cancellationToken).ConfigureAwait(false);

        await _libraryCatalog.UpsertAsync(new LibraryCatalogEntry(
            Source: normalizedSource,
            SourceId: persistedSourceId,
            Title: metadata.Title,
            Artist: metadata.Artist,
            Charter: metadata.Charter,
            LocalPath: finalPath,
            AddedAtUtc: DateTimeOffset.UtcNow,
            ExternalKeyHash: LibraryIdentityService.BuildExternalKeyHash(persistedSourceId),
            InternalIdentityKey: internalIdentityKey,
            ContentIdentityHash: contentIdentityHash), cancellationToken).ConfigureAwait(false);

        return (true, renamed);
    }
}
