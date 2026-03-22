using System.Security.Cryptography;

using ChartHub.Models;
using ChartHub.Utilities;

using SharpCompress.Archives;
using SharpCompress.Common;

namespace ChartHub.Services;

public interface ISongInstallService
{
    Task<IReadOnlyList<string>> InstallSelectedDownloadsAsync(
        IEnumerable<string> selectedFilePaths,
        IProgress<InstallProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class SongInstallService : ISongInstallService
{
    private readonly AppGlobalSettings _settings;
    private readonly SongIngestionCatalogService _ingestionCatalog;
    private readonly SongIngestionStateMachine _ingestionStateMachine;
    private readonly IOnyxPipelineService _onyxPipelineService;
    private readonly ISongIniMetadataParser _songIniMetadataParser;
    private readonly ICloneHeroDirectorySchemaService _schemaService;
    private readonly LibraryCatalogService? _libraryCatalog;

    public SongInstallService(
        AppGlobalSettings settings,
        SongIngestionCatalogService ingestionCatalog,
        SongIngestionStateMachine ingestionStateMachine,
        IOnyxPipelineService onyxPipelineService,
        ISongIniMetadataParser songIniMetadataParser,
        ICloneHeroDirectorySchemaService schemaService,
        LibraryCatalogService? libraryCatalog)
    {
        _settings = settings;
        _ingestionCatalog = ingestionCatalog;
        _ingestionStateMachine = ingestionStateMachine;
        _onyxPipelineService = onyxPipelineService;
        _songIniMetadataParser = songIniMetadataParser;
        _schemaService = schemaService;
        _libraryCatalog = libraryCatalog;
    }

    public async Task<IReadOnlyList<string>> InstallSelectedDownloadsAsync(
        IEnumerable<string> selectedFilePaths,
        IProgress<InstallProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var files = selectedFilePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var installedDirectories = new List<string>();
        if (files.Count == 0)
        {
            return installedDirectories;
        }

        for (int index = 0; index < files.Count; index++)
        {
            string filePath = files[index];
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(filePath))
            {
                continue;
            }

            double overallStart = index * 100d / files.Count;
            double overallEnd = (index + 1) * 100d / files.Count;
            string itemName = Path.GetFileName(filePath);
            progress?.Report(new InstallProgressUpdate(
                InstallStage.Preparing,
                $"Preparing install for {itemName}",
                overallStart,
                itemName));

            SongIngestionRecord? ingestion = await _ingestionCatalog.GetLatestIngestionByAssetLocationAsync(filePath, cancellationToken);
            string source = _schemaService.NormalizeSource(ingestion?.Source);
            SongIngestionAttemptRecord? attempt = null;
            IngestionState state = ingestion?.CurrentState ?? IngestionState.Downloaded;

            if (ingestion is not null)
            {
                attempt = await _ingestionCatalog.StartAttemptAsync(ingestion.Id, cancellationToken);
                state = await TransitionAsync(
                    ingestion.Id,
                    attempt.Id,
                    state,
                    IngestionState.Installing,
                    "Install started",
                    cancellationToken);
            }

            try
            {
                var itemProgress = new ScaledInstallProgress(progress, overallStart, overallEnd, itemName);
                List<InstalledSongResult> installedResults = await InstallFileAsync(filePath, source, itemProgress, cancellationToken);
                installedDirectories.AddRange(installedResults.Select(result => result.DirectoryPath));

                foreach (InstalledSongResult installedResult in installedResults)
                {
                    await UpsertLibraryEntryAsync(
                    installedResult.DirectoryPath,
                        source,
                        ingestion?.SourceId,
                        Path.GetFileNameWithoutExtension(filePath),
                    cancellationToken,
                    installedResult.Metadata);
                }

                if (ingestion is not null && attempt is not null)
                {
                    foreach (InstalledSongResult installedResult in installedResults)
                    {
                        await _ingestionCatalog.UpsertAssetAsync(new SongIngestionAssetEntry(
                            IngestionId: ingestion.Id,
                            AttemptId: attempt.Id,
                            AssetRole: IngestionAssetRole.InstalledDirectory,
                            Location: installedResult.DirectoryPath,
                            SizeBytes: null,
                            ContentHash: null,
                            RecordedAtUtc: DateTimeOffset.UtcNow), cancellationToken);

                        await WriteManifestAsync(ingestion.Id, attempt.Id, installedResult.DirectoryPath, cancellationToken);
                    }

                    _ = await TransitionAsync(
                        ingestion.Id,
                        attempt.Id,
                        state,
                        IngestionState.Installed,
                        "Install completed",
                        cancellationToken);
                }

                File.Delete(filePath);
                progress?.Report(new InstallProgressUpdate(
                    InstallStage.Completed,
                    $"Installed {itemName}",
                    overallEnd,
                    itemName));
            }
            catch (OperationCanceledException)
            {
                progress?.Report(new InstallProgressUpdate(
                    InstallStage.Cancelled,
                    $"Install cancelled while processing {itemName}",
                    null,
                    itemName));
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogError("Install", "Failed to install selected download", ex, new Dictionary<string, object?>
                {
                    ["filePath"] = filePath,
                    ["source"] = source,
                });

                if (ingestion is not null && attempt is not null)
                {
                    _ = await TransitionAsync(
                        ingestion.Id,
                        attempt.Id,
                        state,
                        IngestionState.Failed,
                        ex.Message,
                        cancellationToken);
                }

                progress?.Report(new InstallProgressUpdate(
                    InstallStage.Failed,
                    $"Failed to install {itemName}: {ex.Message}",
                    null,
                    itemName,
                    ex.Message));
            }
        }

        progress?.Report(new InstallProgressUpdate(
            InstallStage.Completed,
            "Install session finished",
            100,
            files.Count == 1 ? Path.GetFileName(files[0]) : $"{files.Count} items"));

        return installedDirectories;
    }

    private async Task<List<InstalledSongResult>> InstallFileAsync(
        string filePath,
        string source,
        IProgress<InstallProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_settings.CloneHeroSongsDir);
        Directory.CreateDirectory(_settings.OutputDir);

        var installedDirs = new List<InstalledSongResult>();
        string extension = Path.GetExtension(filePath).ToLowerInvariant();

        if (extension is ".zip" or ".rar" or ".7z")
        {
            string installedDirectory = await InstallArchiveFileAsync(filePath, source, progress, cancellationToken);
            installedDirs.Add(new InstalledSongResult(installedDirectory, null));
            return installedDirs;
        }

        OnyxInstallResult onyxResult = await _onyxPipelineService.InstallAsync(filePath, source, progress, cancellationToken);
        string canonicalDir = await RehomeInstalledDirectoryAsync(
            onyxResult.FinalInstallDirectory,
            source,
            Path.GetFileNameWithoutExtension(filePath),
            onyxResult.ParsedMetadata);
        installedDirs.Add(new InstalledSongResult(canonicalDir, onyxResult.ParsedMetadata));

        return installedDirs;
    }

    private async Task<string> InstallArchiveFileAsync(
        string filePath,
        string source,
        IProgress<InstallProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        string sanitizedName = SafePathHelper.SanitizeFileName(Path.GetFileNameWithoutExtension(filePath), "song");
        string targetDir = ResolveUniqueDirectory(Path.Combine(_settings.CloneHeroSongsDir, $"{sanitizedName}__{source}"));
        Directory.CreateDirectory(targetDir);

        using IArchive archive = FileTools.OpenArchive(filePath);
        var entries = archive.Entries.Where(entry => !entry.IsDirectory).ToList();
        for (int index = 0; index < entries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IArchiveEntry entry = entries[index];
            double progressValue = entries.Count == 0 ? 50 : 10 + ((index + 1) * 70d / entries.Count);
            progress?.Report(new InstallProgressUpdate(
                InstallStage.ExtractingArchive,
                $"Extracting {entry.Key}",
                progressValue,
                Path.GetFileName(filePath),
                entry.Key));

            string destinationPath = SafePathHelper.GetSafeArchiveExtractionPath(targetDir, entry.Key, "archive-entry");
            string? destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            await Task.Run(() => entry.WriteToFile(destinationPath, new ExtractionOptions
            {
                ExtractFullPath = false,
                Overwrite = true,
            }), cancellationToken);
        }

        progress?.Report(new InstallProgressUpdate(
            InstallStage.Completed,
            $"Archive install completed for {Path.GetFileName(filePath)}",
            100,
            Path.GetFileName(filePath)));

        return await RehomeInstalledDirectoryAsync(targetDir, source, Path.GetFileNameWithoutExtension(filePath));
    }

    private Task<string> RehomeInstalledDirectoryAsync(
        string currentDirectory,
        string source,
        string? fallbackTitle,
        SongMetadata? fallbackMetadata = null)
    {
        if (!Directory.Exists(currentDirectory))
        {
            return Task.FromResult(currentDirectory);
        }

        string? songIniPath = Directory
            .EnumerateFiles(currentDirectory, "*", SearchOption.AllDirectories)
            .FirstOrDefault(path => Path.GetFileName(path).Equals("song.ini", StringComparison.OrdinalIgnoreCase));

        SongMetadata metadata = songIniPath is not null
            ? _songIniMetadataParser.ParseFromSongIni(songIniPath)
            : fallbackMetadata ?? new SongMetadata("Unknown Artist", fallbackTitle ?? "Unknown Song", "Unknown Charter");

        CloneHeroDirectoryLayout layout = _schemaService.ResolveUniqueLayout(
            _settings.CloneHeroSongsDir,
            metadata,
            source,
            exists: path => string.Equals(path, currentDirectory, StringComparison.Ordinal) || Directory.Exists(path));

        if (string.Equals(layout.FullPath, currentDirectory, StringComparison.Ordinal))
        {
            return Task.FromResult(currentDirectory);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(layout.FullPath)!);
        Directory.Move(currentDirectory, layout.FullPath);
        return Task.FromResult(layout.FullPath);
    }

    private static bool IsTrackableSource(string source) =>
        string.Equals(source, LibrarySourceNames.RhythmVerse, StringComparison.OrdinalIgnoreCase)
        || string.Equals(source, LibrarySourceNames.Encore, StringComparison.OrdinalIgnoreCase);

    private async Task UpsertLibraryEntryAsync(
        string installDir,
        string source,
        string? sourceId,
        string? fallbackTitle,
        CancellationToken cancellationToken,
        SongMetadata? fallbackMetadata = null)
    {
        if (_libraryCatalog is null)
        {
            return;
        }

        // Only persist library entries for songs from known, trusted sources.
        // Local file and cloud drive installs must not pollute the library catalog.
        if (!IsTrackableSource(source))
        {
            return;
        }

        string? songIniPath = Directory
            .EnumerateFiles(installDir, "*", SearchOption.AllDirectories)
            .FirstOrDefault(path => Path.GetFileName(path).Equals("song.ini", StringComparison.OrdinalIgnoreCase));

        SongMetadata metadata = songIniPath is not null
            ? _songIniMetadataParser.ParseFromSongIni(songIniPath)
            : fallbackMetadata ?? new SongMetadata("Unknown Artist", fallbackTitle ?? "Unknown Song", "Unknown Charter");

        string persistedSource = _schemaService.NormalizeSource(source);
        string contentIdentityHash = await LibraryIdentityService.ComputeInstalledContentIdentityHashAsync(installDir, cancellationToken);
        string persistedSourceId = string.IsNullOrWhiteSpace(sourceId)
            ? LibraryIdentityService.BuildInternalIdentityKey(contentIdentityHash, metadata)
            : LibraryIdentityService.NormalizeSourceKey(persistedSource, sourceId);
        string internalIdentityKey = LibraryIdentityService.BuildInternalIdentityKey(contentIdentityHash, metadata);

        await _libraryCatalog.RemoveOtherEntriesByLocalPathAsync(
            installDir,
            persistedSource,
            persistedSourceId,
            cancellationToken);

        await _libraryCatalog.UpsertAsync(new LibraryCatalogEntry(
            Source: persistedSource,
            SourceId: persistedSourceId,
            Title: metadata.Title,
            Artist: metadata.Artist,
            Charter: metadata.Charter,
            LocalPath: installDir,
                AddedAtUtc: DateTimeOffset.UtcNow,
                ExternalKeyHash: LibraryIdentityService.BuildExternalKeyHash(persistedSourceId),
                InternalIdentityKey: internalIdentityKey,
                ContentIdentityHash: contentIdentityHash), cancellationToken);
    }

    private async Task WriteManifestAsync(long ingestionId, long attemptId, string installDir, CancellationToken cancellationToken)
    {
        foreach (string filePath in Directory.GetFiles(installDir, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileInfo = new FileInfo(filePath);
            string relativePath = Path.GetRelativePath(installDir, filePath);
            string sha256 = await ComputeSha256Async(filePath, cancellationToken);

            await _ingestionCatalog.UpsertManifestFileAsync(new SongInstalledManifestFileEntry(
                IngestionId: ingestionId,
                AttemptId: attemptId,
                InstallRoot: installDir,
                RelativePath: relativePath,
                Sha256: sha256,
                SizeBytes: fileInfo.Length,
                LastWriteUtc: new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero),
                RecordedAtUtc: DateTimeOffset.UtcNow), cancellationToken);
        }
    }

    private async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        byte[] hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<IngestionState> TransitionAsync(
        long ingestionId,
        long attemptId,
        IngestionState currentState,
        IngestionState targetState,
        string details,
        CancellationToken cancellationToken)
    {
        if (currentState == targetState)
        {
            return currentState;
        }

        if (_ingestionStateMachine.CanTransition(currentState, targetState))
        {
            await _ingestionCatalog.RecordStateTransitionAsync(
                ingestionId,
                attemptId,
                currentState,
                targetState,
                details,
                cancellationToken);
            return targetState;
        }

        if (_ingestionStateMachine.CanTransition(currentState, IngestionState.Queued)
            && _ingestionStateMachine.CanTransition(IngestionState.Queued, targetState))
        {
            await _ingestionCatalog.RecordStateTransitionAsync(
                ingestionId,
                attemptId,
                currentState,
                IngestionState.Queued,
                "Install transition reset",
                cancellationToken);

            await _ingestionCatalog.RecordStateTransitionAsync(
                ingestionId,
                attemptId,
                IngestionState.Queued,
                targetState,
                details,
                cancellationToken);

            return targetState;
        }

        return currentState;
    }

    private static string ResolveUniqueDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return path;
        }

        string parent = Path.GetDirectoryName(path) ?? string.Empty;
        string baseName = Path.GetFileName(path);
        int counter = 2;

        while (true)
        {
            string candidate = Path.Combine(parent, $"{baseName}_{counter}");
            if (!Directory.Exists(candidate))
            {
                return candidate;
            }

            counter++;
        }
    }

    private sealed record InstalledSongResult(
        string DirectoryPath,
        SongMetadata? Metadata);

    private sealed class ScaledInstallProgress : IProgress<InstallProgressUpdate>
    {
        private readonly IProgress<InstallProgressUpdate>? _inner;
        private readonly double _startPercent;
        private readonly double _endPercent;
        private readonly string _currentItemName;

        public ScaledInstallProgress(IProgress<InstallProgressUpdate>? inner, double startPercent, double endPercent, string currentItemName)
        {
            _inner = inner;
            _startPercent = startPercent;
            _endPercent = endPercent;
            _currentItemName = currentItemName;
        }

        public void Report(InstallProgressUpdate value)
        {
            if (_inner is null)
            {
                return;
            }

            double? scaledPercent = null;
            if (value.ProgressPercent.HasValue)
            {
                double normalized = Math.Clamp(value.ProgressPercent.Value, 0, 100);
                scaledPercent = _startPercent + ((_endPercent - _startPercent) * (normalized / 100d));
            }

            _inner.Report(value with
            {
                ProgressPercent = scaledPercent,
                CurrentItemName = string.IsNullOrWhiteSpace(value.CurrentItemName) ? _currentItemName : value.CurrentItemName,
            });
        }
    }
}
