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

public sealed class SongInstallService(
    AppGlobalSettings settings,
    SongIngestionCatalogService ingestionCatalog,
    SongIngestionStateMachine ingestionStateMachine,
    IOnyxPipelineService onyxPipelineService) : ISongInstallService
{
    private readonly AppGlobalSettings _settings = settings;
    private readonly SongIngestionCatalogService _ingestionCatalog = ingestionCatalog;
    private readonly SongIngestionStateMachine _ingestionStateMachine = ingestionStateMachine;
    private readonly IOnyxPipelineService _onyxPipelineService = onyxPipelineService;

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
            return installedDirectories;

        for (var index = 0; index < files.Count; index++)
        {
            var filePath = files[index];
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(filePath))
                continue;

            var overallStart = index * 100d / files.Count;
            var overallEnd = (index + 1) * 100d / files.Count;
            var itemName = Path.GetFileName(filePath);
            progress?.Report(new InstallProgressUpdate(
                InstallStage.Preparing,
                $"Preparing install for {itemName}",
                overallStart,
                itemName));

            var ingestion = await _ingestionCatalog.GetLatestIngestionByAssetLocationAsync(filePath, cancellationToken);
            var sourceSuffix = NormalizeSourceSuffix(ingestion?.Source);
            SongIngestionAttemptRecord? attempt = null;
            var state = ingestion?.CurrentState ?? IngestionState.Downloaded;

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
                var fileInstalledDirs = await InstallFileAsync(filePath, sourceSuffix, itemProgress, cancellationToken);
                installedDirectories.AddRange(fileInstalledDirs);

                if (ingestion is not null && attempt is not null)
                {
                    foreach (var installDir in fileInstalledDirs)
                    {
                        await _ingestionCatalog.UpsertAssetAsync(new SongIngestionAssetEntry(
                            IngestionId: ingestion.Id,
                            AttemptId: attempt.Id,
                            AssetRole: IngestionAssetRole.InstalledDirectory,
                            Location: installDir,
                            SizeBytes: null,
                            ContentHash: null,
                            RecordedAtUtc: DateTimeOffset.UtcNow), cancellationToken);

                        await WriteManifestAsync(ingestion.Id, attempt.Id, installDir, cancellationToken);
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
                    ["sourceSuffix"] = sourceSuffix,
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

    private async Task<List<string>> InstallFileAsync(
        string filePath,
        string sourceSuffix,
        IProgress<InstallProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_settings.CloneHeroSongsDir);
        Directory.CreateDirectory(_settings.OutputDir);

        var installedDirs = new List<string>();
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        if (extension is ".zip" or ".rar" or ".7z")
        {
            installedDirs.Add(await InstallArchiveFileAsync(filePath, sourceSuffix, progress, cancellationToken));
            return installedDirs;
        }

        var onyxResult = await _onyxPipelineService.InstallAsync(filePath, sourceSuffix, progress, cancellationToken);
        installedDirs.Add(onyxResult.FinalInstallDirectory);

        return installedDirs;
    }

    private async Task<string> InstallArchiveFileAsync(
        string filePath,
        string sourceSuffix,
        IProgress<InstallProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var sanitizedName = SafePathHelper.SanitizeFileName(Path.GetFileNameWithoutExtension(filePath), "song");
        var targetDir = ResolveUniqueDirectory(Path.Combine(_settings.CloneHeroSongsDir, $"{sanitizedName}__{sourceSuffix}"));
        Directory.CreateDirectory(targetDir);

        using var archive = FileTools.OpenArchive(filePath);
        var entries = archive.Entries.Where(entry => !entry.IsDirectory).ToList();
        for (var index = 0; index < entries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries[index];
            var progressValue = entries.Count == 0 ? 50 : 10 + ((index + 1) * 70d / entries.Count);
            progress?.Report(new InstallProgressUpdate(
                InstallStage.ExtractingArchive,
                $"Extracting {entry.Key}",
                progressValue,
                Path.GetFileName(filePath),
                entry.Key));

            var destinationPath = SafePathHelper.GetSafeArchiveExtractionPath(targetDir, entry.Key, "archive-entry");
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);

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

        return targetDir;
    }

    private async Task WriteManifestAsync(long ingestionId, long attemptId, string installDir, CancellationToken cancellationToken)
    {
        foreach (var filePath in Directory.GetFiles(installDir, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileInfo = new FileInfo(filePath);
            var relativePath = Path.GetRelativePath(installDir, filePath);
            var sha256 = await ComputeSha256Async(filePath, cancellationToken);

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
        await using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
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
            return currentState;

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

    private static string NormalizeSourceSuffix(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return "unknown";

        return source.Trim().ToLowerInvariant();
    }

    private static string ResolveUniqueDirectory(string path)
    {
        if (!Directory.Exists(path))
            return path;

        var parent = Path.GetDirectoryName(path) ?? string.Empty;
        var baseName = Path.GetFileName(path);
        var counter = 2;

        while (true)
        {
            var candidate = Path.Combine(parent, $"{baseName}_{counter}");
            if (!Directory.Exists(candidate))
                return candidate;

            counter++;
        }
    }

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
                return;

            double? scaledPercent = null;
            if (value.ProgressPercent.HasValue)
            {
                var normalized = Math.Clamp(value.ProgressPercent.Value, 0, 100);
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
