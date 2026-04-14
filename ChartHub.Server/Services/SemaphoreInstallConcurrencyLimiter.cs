namespace ChartHub.Server.Services;

/// <summary>
/// Limits concurrent install pipeline executions to a fixed maximum.
/// </summary>
public sealed class SemaphoreInstallConcurrencyLimiter : IInstallConcurrencyLimiter, IDisposable
{
    private const int MaxConcurrentInstalls = 2;
    private readonly SemaphoreSlim _semaphore = new(MaxConcurrentInstalls, MaxConcurrentInstalls);

    public Task WaitAsync(CancellationToken cancellationToken) =>
        _semaphore.WaitAsync(cancellationToken);

    public void Release() => _semaphore.Release();

    public void Dispose() => _semaphore.Dispose();
}
