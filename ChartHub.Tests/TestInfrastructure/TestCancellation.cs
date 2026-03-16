namespace ChartHub.Tests.TestInfrastructure;

public static class TestCancellation
{
    public static CancellationToken AlreadyCancelledToken()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        return cts.Token;
    }

    public static CancellationTokenSource CancelAfter(TimeSpan timeout)
    {
        var cts = new CancellationTokenSource();
        cts.CancelAfter(timeout);
        return cts;
    }
}
