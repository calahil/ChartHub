using System.Collections.ObjectModel;

using ChartHub.Models;

namespace ChartHub.Services.Transfers;

public interface ITransferOrchestrator
{
    Task<TransferResult> QueueSongDownloadAsync(
        ViewSong song,
        DownloadFile? downloadItem,
        ObservableCollection<DownloadFile> downloads,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> DownloadSelectedCloudFilesToLocalAsync(
        IEnumerable<WatcherFile> selectedCloudFiles,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> SyncCloudToLocalAdditiveAsync(
        IEnumerable<WatcherFile> currentCloudFiles,
        CancellationToken cancellationToken = default);
}
