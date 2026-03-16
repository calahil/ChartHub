using System.Collections.ObjectModel;

namespace ChartHub.Services;

public sealed class SharedDownloadQueue
{
    public ObservableCollection<DownloadFile> Downloads { get; } = [];
}