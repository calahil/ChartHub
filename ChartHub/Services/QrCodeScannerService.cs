namespace ChartHub.Services;

public interface IQrCodeScannerService
{
    bool IsSupported { get; }
    Task<string?> ScanAsync(CancellationToken cancellationToken = default);
}

public sealed class NoOpQrCodeScannerService : IQrCodeScannerService
{
    public bool IsSupported => false;

    public Task<string?> ScanAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(null);
    }
}
