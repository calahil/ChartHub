using ChartHub.Server.Contracts;

namespace ChartHub.Server.Services;

/// <summary>
/// One-shot startup service that repairs two classes of previously-installed songs:
///
/// 1. Duplicate installs: archives that were installed more than once for the same
///    (artist, title, charter, source) group because the original install produced a
///    nested subdirectory and a second install attempt created a _N-suffixed sibling.
///    The _N-suffixed directories and their job records are deleted; the original is kept.
///
/// 2. Nested subdirectories: install directories whose chart files are inside one extra
///    subdirectory instead of living directly in the Charter__source folder.  The files
///    are moved up one level and the empty subdirectory is removed.  The DB records do
///    not need updating because installed_path already points at the correct final folder.
/// </summary>
public sealed partial class NestedInstallPathMigrationService(
    IDownloadJobStore jobStore,
    ILogger<NestedInstallPathMigrationService> logger) : IHostedService
{
    private readonly IDownloadJobStore _jobStore = jobStore;
    private readonly ILogger<NestedInstallPathMigrationService> _logger = logger;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<DownloadJobResponse> installed = _jobStore.List()
                .Where(j => string.Equals(j.Stage, "Installed", StringComparison.Ordinal) && j.InstalledPath is not null)
                .ToList();

            int duplicatesRemoved = RemoveDuplicateJobs(installed);
            if (duplicatesRemoved > 0)
            {
                LogDuplicatesRemoved(_logger, duplicatesRemoved);
            }

            // Re-fetch after duplicate removal so we only flatten surviving records.
            IReadOnlyList<DownloadJobResponse> surviving = _jobStore.List()
                .Where(j => string.Equals(j.Stage, "Installed", StringComparison.Ordinal) && j.InstalledPath is not null)
                .ToList();

            int pathsFlattened = FlattenNestedPaths(surviving);
            if (pathsFlattened > 0)
            {
                LogPathsFlattened(_logger, pathsFlattened);
            }
        }
        catch (Exception ex)
        {
            LogMigrationFailed(_logger, ex);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private int RemoveDuplicateJobs(IReadOnlyList<DownloadJobResponse> installedJobs)
    {
        // Group by the tuple that should be unique: (artist, title, charter, source).
        // Normalise to lower-case to avoid case sensitivity false mismatches.
        IEnumerable<IGrouping<(string Artist, string Title, string Charter, string Source), DownloadJobResponse>> groups = installedJobs
            .GroupBy(j => (
                Artist: (j.Artist ?? string.Empty).ToLowerInvariant(),
                Title: (j.Title ?? string.Empty).ToLowerInvariant(),
                Charter: (j.Charter ?? string.Empty).ToLowerInvariant(),
                Source: j.Source.ToLowerInvariant()))
            .Where(g => g.Count() > 1);

        int removed = 0;
        foreach (IGrouping<(string Artist, string Title, string Charter, string Source), DownloadJobResponse> group in groups)
        {
            // Separate "original" records (no _N suffix) from duplicates (_N suffix).
            var original = group
                .Where(j => !HasNumericSuffix(j.InstalledPath!))
                .ToList();

            var duplicates = group
                .Where(j => HasNumericSuffix(j.InstalledPath!))
                .OrderBy(j => ExtractSuffix(j.InstalledPath!))
                .ToList();

            List<DownloadJobResponse> toDelete;

            if (original.Count > 0)
            {
                // Keep the first original; delete all _N entries.
                toDelete = duplicates;
                if (original.Count > 1)
                {
                    // If there are multiple originals (shouldn't happen) keep the oldest.
                    toDelete = toDelete.Concat(original.OrderByDescending(j => j.CreatedAtUtc)).Take(original.Count - 1).ToList();
                }
            }
            else
            {
                // No clean original exists; keep lowest _N suffix, delete the rest.
                toDelete = duplicates.Skip(1).ToList();
            }

            foreach (DownloadJobResponse job in toDelete)
            {
                string installedPath = job.InstalledPath!;
                LogRemovingDuplicate(_logger, job.JobId, installedPath);

                try
                {
                    if (Directory.Exists(installedPath))
                    {
                        Directory.Delete(installedPath, recursive: true);
                    }
                }
                catch (Exception ex)
                {
                    LogDeleteDirectoryFailed(_logger, installedPath, ex);
                }

                try
                {
                    _jobStore.DeleteJob(job.JobId);
                    removed++;
                }
                catch (Exception ex)
                {
                    LogDeleteJobFailed(_logger, job.JobId, ex);
                }
            }
        }

        return removed;
    }

    private int FlattenNestedPaths(IReadOnlyList<DownloadJobResponse> installedJobs)
    {
        int flattened = 0;
        foreach (DownloadJobResponse job in installedJobs)
        {
            string installedPath = job.InstalledPath!;
            if (!Directory.Exists(installedPath))
            {
                continue;
            }

            string[] directFiles = Directory.GetFiles(installedPath);
            string[] subdirectories = Directory.GetDirectories(installedPath);

            // A nested install has exactly one subdirectory and no direct files.
            if (directFiles.Length != 0 || subdirectories.Length != 1)
            {
                continue;
            }

            string nestedDir = subdirectories[0];
            LogFlatteningPath(_logger, job.JobId, installedPath, nestedDir);

            try
            {
                foreach (string file in Directory.EnumerateFiles(nestedDir, "*", SearchOption.AllDirectories))
                {
                    string relativePart = Path.GetRelativePath(nestedDir, file);
                    string destination = Path.Combine(installedPath, relativePart);
                    string? destDir = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrWhiteSpace(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    File.Move(file, destination, overwrite: false);
                }

                Directory.Delete(nestedDir, recursive: false);
                flattened++;
            }
            catch (Exception ex)
            {
                LogFlattenFailed(_logger, installedPath, ex);
            }
        }

        return flattened;
    }

    /// <summary>Returns true if the last path segment ends with an underscore followed by digits, e.g. "pksage__rhythmverse_2".</summary>
    private static bool HasNumericSuffix(string installedPath)
    {
        string segment = GetLastSegment(installedPath);
        int underscoreIndex = segment.LastIndexOf('_');
        if (underscoreIndex < 0 || underscoreIndex == segment.Length - 1)
        {
            return false;
        }

        ReadOnlySpan<char> suffix = segment.AsSpan(underscoreIndex + 1);
        foreach (char c in suffix)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    private static int ExtractSuffix(string installedPath)
    {
        string segment = GetLastSegment(installedPath);
        int underscoreIndex = segment.LastIndexOf('_');
        if (underscoreIndex < 0)
        {
            return 0;
        }

        return int.TryParse(segment.AsSpan(underscoreIndex + 1), out int value) ? value : 0;
    }

    private static string GetLastSegment(string path)
    {
        // Trim any trailing separator so GetFileName returns the last segment reliably.
        return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? string.Empty;
    }

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "Nested install path migration: removed {Count} duplicate job(s).")]
    private static partial void LogDuplicatesRemoved(ILogger logger, int count);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "Nested install path migration: flattened {Count} nested path(s).")]
    private static partial void LogPathsFlattened(ILogger logger, int count);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Warning, Message = "Nested install path migration: removing duplicate job {JobId} at '{InstalledPath}'.")]
    private static partial void LogRemovingDuplicate(ILogger logger, Guid jobId, string installedPath);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Warning, Message = "Nested install path migration: flattening '{InstalledPath}' (job {JobId}, nested dir '{NestedDir}').")]
    private static partial void LogFlatteningPath(ILogger logger, Guid jobId, string installedPath, string nestedDir);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Error, Message = "Nested install path migration: failed to delete directory '{InstalledPath}'.")]
    private static partial void LogDeleteDirectoryFailed(ILogger logger, string installedPath, Exception exception);

    [LoggerMessage(EventId = 3006, Level = LogLevel.Error, Message = "Nested install path migration: failed to delete job {JobId} from store.")]
    private static partial void LogDeleteJobFailed(ILogger logger, Guid jobId, Exception exception);

    [LoggerMessage(EventId = 3007, Level = LogLevel.Error, Message = "Nested install path migration: failed to flatten '{InstalledPath}'.")]
    private static partial void LogFlattenFailed(ILogger logger, string installedPath, Exception exception);

    [LoggerMessage(EventId = 3008, Level = LogLevel.Error, Message = "Nested install path migration failed unexpectedly.")]
    private static partial void LogMigrationFailed(ILogger logger, Exception exception);
}
