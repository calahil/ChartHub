namespace ChartHub.Server.Services;

public sealed partial class DesktopEntryStartupHostedService(
    IDesktopEntryService desktopEntryService,
    ILogger<DesktopEntryStartupHostedService> logger) : IHostedService
{
    private readonly IDesktopEntryService _desktopEntryService = desktopEntryService;
    private readonly ILogger<DesktopEntryStartupHostedService> _logger = logger;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_desktopEntryService.IsEnabled)
        {
            return;
        }

        if (!_desktopEntryService.IsSupportedPlatform)
        {
            LogSkippingUnsupportedPlatform(_logger);
            return;
        }

        try
        {
            await _desktopEntryService.RefreshCatalogAsync(cancellationToken).ConfigureAwait(false);
            int count = (await _desktopEntryService.ListEntriesAsync(cancellationToken).ConfigureAwait(false)).Count;
            LogCatalogLoaded(_logger, count);
        }
        catch (Exception ex)
        {
            LogCatalogLoadFailed(_logger, ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        EventId = 7201,
        Level = LogLevel.Information,
        Message = "DesktopEntry startup catalog loaded. Count={Count}")]
    private static partial void LogCatalogLoaded(ILogger logger, int count);

    [LoggerMessage(
        EventId = 7202,
        Level = LogLevel.Warning,
        Message = "DesktopEntry startup scan skipped because host is not Linux.")]
    private static partial void LogSkippingUnsupportedPlatform(ILogger logger);

    [LoggerMessage(
        EventId = 7203,
        Level = LogLevel.Error,
        Message = "DesktopEntry startup catalog load failed.")]
    private static partial void LogCatalogLoadFailed(ILogger logger, Exception exception);
}
