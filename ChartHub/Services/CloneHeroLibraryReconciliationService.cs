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

        var songIniDirectories = Directory
            .EnumerateFiles(_settings.CloneHeroSongsDir, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path).Equals("song.ini", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetDirectoryName(path))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();

        var scanned = 0;
        var updated = 0;
        var renamed = 0;
        var failed = 0;

        for (var i = 0; i < songIniDirectories.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var songDirectory = songIniDirectories[i];

            progress?.Report(new CloneHeroReconciliationProgress(
                Message: $"Reconciling {Path.GetFileName(songDirectory)}",
                ProcessedSongs: i,
                TotalSongs: songIniDirectories.Count,
                ProgressPercent: songIniDirectories.Count == 0 ? null : (i * 100d / songIniDirectories.Count)));

            scanned++;

            try
            {
                var before = songDirectory;
                var after = await ReconcileSongDirectoryCoreAsync(songDirectory, cancellationToken);
                updated++;

                if (!string.Equals(before, after, StringComparison.Ordinal))
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

        progress?.Report(new CloneHeroReconciliationProgress(
            Message: "Reconciliation complete",
            ProcessedSongs: songIniDirectories.Count,
            TotalSongs: songIniDirectories.Count,
            ProgressPercent: 100));

        return new CloneHeroReconciliationResult(
            Scanned: scanned,
            Updated: updated,
            Renamed: renamed,
            Failed: failed);
    }

    public async Task<bool> ReconcileSongDirectoryAsync(string songDirectory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(songDirectory) || !Directory.Exists(songDirectory))
            return false;

        await ReconcileSongDirectoryCoreAsync(songDirectory, cancellationToken);
        return true;
    }

    private async Task<string> ReconcileSongDirectoryCoreAsync(string songDirectory, CancellationToken cancellationToken)
    {
        var songIniPath = Directory
            .EnumerateFiles(songDirectory, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => Path.GetFileName(path).Equals("song.ini", StringComparison.OrdinalIgnoreCase));

        var metadata = songIniPath is null
            ? SongMetadata.Unknown
            : _songIniMetadataParser.ParseFromSongIni(songIniPath);

        // Preserve an existing source from DB linkage when available; otherwise classify as import.
        var existingEntry = await _libraryCatalog.GetEntryByLocalPathAsync(songDirectory, cancellationToken)
            .ConfigureAwait(false);
        var source = _schemaService.NormalizeSource(existingEntry?.Source);
        var sourceId = existingEntry?.SourceId;

        var targetLayout = _schemaService.ResolveUniqueLayout(
            _settings.CloneHeroSongsDir,
            metadata,
            source,
            exists: path => string.Equals(path, songDirectory, StringComparison.Ordinal) || Directory.Exists(path));

        var finalDirectory = songDirectory;
        if (!string.Equals(songDirectory, targetLayout.FullPath, StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetLayout.FullPath)!);
            Directory.Move(songDirectory, targetLayout.FullPath);
            finalDirectory = targetLayout.FullPath;
        }

        var contentIdentityHash = await LibraryIdentityService.ComputeInstalledContentIdentityHashAsync(finalDirectory, cancellationToken)
            .ConfigureAwait(false);
        var internalIdentityKey = LibraryIdentityService.BuildInternalIdentityKey(contentIdentityHash, metadata);
        var persistedSourceId = string.IsNullOrWhiteSpace(sourceId)
            ? internalIdentityKey
            : LibraryIdentityService.NormalizeSourceKey(source, sourceId);

        await _libraryCatalog.RemoveOtherEntriesByLocalPathAsync(
            finalDirectory,
            source,
            persistedSourceId,
            cancellationToken).ConfigureAwait(false);

        await _libraryCatalog.UpsertAsync(new LibraryCatalogEntry(
            Source: source,
            SourceId: persistedSourceId,
            Title: metadata.Title,
            Artist: metadata.Artist,
            Charter: metadata.Charter,
            LocalPath: finalDirectory,
            AddedAtUtc: DateTimeOffset.UtcNow,
            ExternalKeyHash: LibraryIdentityService.BuildExternalKeyHash(persistedSourceId),
            InternalIdentityKey: internalIdentityKey,
            ContentIdentityHash: contentIdentityHash), cancellationToken).ConfigureAwait(false);

        return finalDirectory;
    }
}
