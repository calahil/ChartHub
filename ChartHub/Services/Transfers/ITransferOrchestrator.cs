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
}
