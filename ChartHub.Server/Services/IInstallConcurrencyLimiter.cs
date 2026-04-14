namespace ChartHub.Server.Services;

public interface IInstallConcurrencyLimiter
{
    Task WaitAsync(CancellationToken cancellationToken);

    void Release();
}
