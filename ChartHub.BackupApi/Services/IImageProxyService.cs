namespace ChartHub.BackupApi.Services;

public interface IImageProxyService
{
    Task<ImageProxyResult?> GetImageAsync(string imagePath, CancellationToken cancellationToken);
}

public sealed record ImageProxyResult(byte[] Data, string ContentType);