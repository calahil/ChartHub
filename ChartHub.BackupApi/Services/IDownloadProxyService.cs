namespace ChartHub.BackupApi.Services;

public interface IDownloadProxyService
{
    Task<DownloadProxyResult?> GetDownloadFileAsync(string downloadPath, CancellationToken cancellationToken);

    Task<DownloadProxyResult?> GetExternalDownloadAsync(string sourceUrl, CancellationToken cancellationToken);
}

public sealed record DownloadProxyResult(string FilePath, string ContentType);