using System;
using System.Threading;
using System.Threading.Tasks;

using ChartHub.Server.Services;

using Xunit;

namespace ChartHub.Server.Tests;

public sealed class InstallConcurrencyLimiterTests : IDisposable
{
    private readonly SemaphoreInstallConcurrencyLimiter _sut = new();

    public void Dispose()
    {
        _sut.Dispose();
    }

    [Fact]
    public async Task TwoConcurrentAcquisitionsBothProceed()
    {
        await _sut.WaitAsync(CancellationToken.None);
        await _sut.WaitAsync(CancellationToken.None);

        // Two slots acquired — third would block.
        Assert.True(true);

        _sut.Release();
        _sut.Release();
    }

    [Fact]
    public async Task ThirdAcquisitionBlocksUntilRelease()
    {
        await _sut.WaitAsync(CancellationToken.None);
        await _sut.WaitAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task third = _sut.WaitAsync(cts.Token);

        // Should not complete immediately since both slots are taken.
        await Task.Delay(50);
        Assert.False(third.IsCompleted, "Third acquire should be blocked while two slots are taken.");

        // Release one slot — third should proceed within a generous window.
        _sut.Release();
        await third.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(third.IsCompletedSuccessfully, "Third acquire should have proceeded after a slot was released.");

        _sut.Release();
        _sut.Release();
    }

    [Fact]
    public async Task CancelledWhileBlockedThrowsOperationCancelled()
    {
        await _sut.WaitAsync(CancellationToken.None);
        await _sut.WaitAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource();
        Task third = _sut.WaitAsync(cts.Token);

        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => third);

        _sut.Release();
        _sut.Release();
    }
}
