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
        CancellationToken cancellationToken = default);
}

public sealed class SongInstallService(
    AppGlobalSettings settings,
    SongIngestionCatalogService ingestionCatalog,
    SongIngestionStateMachine ingestionStateMachine) : ISongInstallService
{
    private readonly AppGlobalSettings _settings = settings;
    private readonly SongIngestionCatalogService _ingestionCatalog = ingestionCatalog;
    private readonly SongIngestionStateMachine _ingestionStateMachine = ingestionStateMachine;

    public async Task<IReadOnlyList<string>> InstallSelectedDownloadsAsync(
        IEnumerable<string> selectedFilePaths,
        CancellationToken cancellationToken = default)
    {
        var installedDirectories = new List<string>();

        foreach (var filePath in selectedFilePaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(filePath))
                continue;

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
                var fileInstalledDirs = await InstallFileAsync(filePath, sourceSuffix, cancellationToken);
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
            }
        }

        return installedDirectories;
    }

    private async Task<List<string>> InstallFileAsync(string filePath, string sourceSuffix, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_settings.CloneHeroSongsDir);
        Directory.CreateDirectory(_settings.OutputDir);

        var installedDirs = new List<string>();
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        if (extension is ".zip" or ".rar" or ".7z")
        {
            var sanitizedName = SafePathHelper.SanitizeFileName(Path.GetFileNameWithoutExtension(filePath), "song");
            var targetDir = ResolveUniqueDirectory(Path.Combine(_settings.CloneHeroSongsDir, $"{sanitizedName}__{sourceSuffix}"));
            Directory.CreateDirectory(targetDir);

            using var archive = FileTools.OpenArchive(filePath);
            foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destinationPath = SafePathHelper.GetSafeArchiveExtractionPath(targetDir, entry.Key, "archive-entry");
                var destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                    Directory.CreateDirectory(destinationDirectory);

                entry.WriteToFile(destinationPath, new ExtractionOptions
                {
                    ExtractFullPath = false,
                    Overwrite = true,
                });
            }

            installedDirs.Add(targetDir);
            return installedDirs;
        }

        var beforeDirs = Directory.GetDirectories(_settings.OutputDir).ToHashSet(StringComparer.Ordinal);
        _ = new OnyxService(_settings, filePath);
        var afterDirs = Directory.GetDirectories(_settings.OutputDir);

        var newOutputDirs = afterDirs
            .Where(directory => !beforeDirs.Contains(directory))
            .ToList();

        foreach (var outputDir in newOutputDirs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var baseName = SafePathHelper.SanitizeFileName(Path.GetFileName(outputDir), "song");
            var destinationDir = ResolveUniqueDirectory(Path.Combine(_settings.CloneHeroSongsDir, $"{baseName}__{sourceSuffix}"));
            Directory.Move(outputDir, destinationDir);
            installedDirs.Add(destinationDir);
        }

        if (installedDirs.Count == 0)
        {
            var fallbackBase = SafePathHelper.SanitizeFileName(Path.GetFileNameWithoutExtension(filePath), "song");
            var fallbackDestination = ResolveUniqueDirectory(Path.Combine(_settings.CloneHeroSongsDir, $"{fallbackBase}__{sourceSuffix}"));
            Directory.CreateDirectory(fallbackDestination);

            var rootFiles = Directory.GetFiles(_settings.OutputDir);
            foreach (var rootFile in rootFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destinationFile = Path.Combine(fallbackDestination, Path.GetFileName(rootFile));
                File.Move(rootFile, destinationFile, overwrite: true);
            }

            if (Directory.GetFiles(fallbackDestination, "*", SearchOption.AllDirectories).Length > 0)
                installedDirs.Add(fallbackDestination);
        }

        return installedDirs;
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
}
