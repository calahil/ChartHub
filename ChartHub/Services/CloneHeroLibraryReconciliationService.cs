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
        int total = songIniFiles.Count;

        int scanned = 0;
        int updated = 0;
        int renamed = 0;
        int failed = 0;

        foreach (string? songIniPath in songIniFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? songDirectory = Path.GetDirectoryName(songIniPath);
            if (string.IsNullOrWhiteSpace(songDirectory))
            {
                continue;
            }

            scanned++;
            progress?.Report(new CloneHeroReconciliationProgress(
                Message: $"Reconciling {Path.GetFileName(songDirectory)}",
                ProcessedSongs: scanned,
                TotalSongs: total,
                ProgressPercent: total == 0 ? 100 : scanned * 100d / total));

            try
            {
                (bool Updated, bool Renamed) result = await ReconcileSongDirectoryInternalAsync(songDirectory, cancellationToken).ConfigureAwait(false);
                if (!result.Updated)
                {
                    continue;
                }

                updated++;
                if (result.Renamed)
                {
                    renamed++;
                }
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
        {
            return false;
        }

        string songIniPath = Path.Combine(songDirectory, "song.ini");
        if (!File.Exists(songIniPath))
        {
            return false;
        }

        (bool Updated, bool Renamed) result = await ReconcileSongDirectoryInternalAsync(songDirectory, cancellationToken).ConfigureAwait(false);
        return result.Updated;
    }

    public async Task<bool> ReParseMetadataAsync(string songDirectory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(songDirectory) || !Directory.Exists(songDirectory))
        {
            return false;
        }

        string songIniPath = Path.Combine(songDirectory, "song.ini");
        if (!File.Exists(songIniPath))
        {
            return false;
        }

        SongMetadata metadata = _songIniMetadataParser.ParseFromSongIni(songIniPath);
        LibraryCatalogEntry? existingEntry = await _libraryCatalog.GetEntryByLocalPathAsync(songDirectory, cancellationToken).ConfigureAwait(false);
        if (existingEntry is null)
        {
            return false;
        }

        string normalizedSource;
        try
        {
            normalizedSource = _schemaService.NormalizeSource(existingEntry.Source);
        }
        catch (ArgumentException)
        {
            return false;
        }

        string persistedSourceId = existingEntry?.SourceId ?? string.Empty;

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
        string songIniPath = Path.Combine(songDirectory, "song.ini");
        if (!File.Exists(songIniPath))
        {
            return (false, false);
        }

        SongMetadata metadata = _songIniMetadataParser.ParseFromSongIni(songIniPath);
        LibraryCatalogEntry? existingEntry = await _libraryCatalog.GetEntryByLocalPathAsync(songDirectory, cancellationToken).ConfigureAwait(false);

        if (existingEntry is null)
        {
            await QuarantineDirectoryAsync(songDirectory, cancellationToken).ConfigureAwait(false);
            return (true, true);
        }

        string normalizedSource;
        try
        {
            normalizedSource = _schemaService.NormalizeSource(existingEntry.Source);
        }
        catch (ArgumentException)
        {
            await _libraryCatalog.RemoveAsync(existingEntry.Source, existingEntry.SourceId, cancellationToken).ConfigureAwait(false);
            await QuarantineDirectoryAsync(songDirectory, cancellationToken).ConfigureAwait(false);
            return (true, true);
        }

        CloneHeroDirectoryLayout layout = _schemaService.ResolveUniqueLayout(
            _settings.CloneHeroSongsDir,
            metadata,
            normalizedSource,
            exists: path => string.Equals(path, songDirectory, StringComparison.Ordinal) || Directory.Exists(path));

        string finalPath = layout.FullPath;
        bool renamed = false;
        if (!string.Equals(songDirectory, finalPath, StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
            Directory.Move(songDirectory, finalPath);
            renamed = true;
        }

        string contentIdentityHash = await LibraryIdentityService.ComputeInstalledContentIdentityHashAsync(finalPath, cancellationToken).ConfigureAwait(false);
        string internalIdentityKey = LibraryIdentityService.BuildInternalIdentityKey(contentIdentityHash, metadata);
        string persistedSourceId = string.IsNullOrWhiteSpace(existingEntry?.SourceId)
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

    private async Task QuarantineDirectoryAsync(string songDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(songDirectory))
        {
            return;
        }

        string quarantineRoot = Path.Combine(_settings.CloneHeroDataDir, "Quarantine");
        Directory.CreateDirectory(quarantineRoot);

        string leaf = Path.GetFileName(songDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string safeLeaf = SafePathHelper.SanitizeFileName(leaf, "unmanaged-song");
        string target = Path.Combine(quarantineRoot, safeLeaf);
        if (Directory.Exists(target))
        {
            target = Path.Combine(quarantineRoot, $"{safeLeaf}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
        }

        Directory.Move(songDirectory, target);

        Logger.LogWarning("CloneHero", "Quarantined unmanaged song directory", new Dictionary<string, object?>
        {
            ["sourceDirectory"] = songDirectory,
            ["quarantineDirectory"] = target,
        });

        await Task.CompletedTask;
    }
}
