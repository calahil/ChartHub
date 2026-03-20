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
    LibraryCatalogService libraryCatalog) : ICloneHeroLibraryReconciliationService
{
    private readonly AppGlobalSettings _settings = settings;
    private readonly LibraryCatalogService _libraryCatalog = libraryCatalog;

    public async Task<CloneHeroReconciliationResult> ReconcileAsync(
        IProgress<CloneHeroReconciliationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_settings.CloneHeroSongsDir);

        await _libraryCatalog.RemoveDuplicateLocalPathEntriesUnderRootAsync(
            _settings.CloneHeroSongsDir,
            cancellationToken).ConfigureAwait(false);

        return new CloneHeroReconciliationResult(Scanned: 0, Updated: 0, Renamed: 0, Failed: 0);
    }

    public Task<bool> ReconcileSongDirectoryAsync(string songDirectory, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }
}
