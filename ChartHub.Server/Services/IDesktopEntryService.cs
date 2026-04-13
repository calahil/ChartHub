using ChartHub.Server.Contracts;

namespace ChartHub.Server.Services;

public interface IDesktopEntryService
{
    bool IsEnabled { get; }

    bool IsSupportedPlatform { get; }

    int SseIntervalSeconds { get; }

    Task RefreshCatalogAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<DesktopEntryItemResponse>> ListEntriesAsync(CancellationToken cancellationToken);

    Task<DesktopEntryActionResponse> ExecuteAsync(string entryId, CancellationToken cancellationToken);

    Task<DesktopEntryActionResponse> KillAsync(string entryId, CancellationToken cancellationToken);

    bool TryResolveIconFile(string entryId, string fileName, out string iconPath, out string contentType);
}
