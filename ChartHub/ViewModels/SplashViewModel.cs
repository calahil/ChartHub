namespace ChartHub.ViewModels;

public class SplashViewModel
{
    private readonly Func<Task> _onComplete;

    public SplashViewModel(Func<Task> onComplete)
    {
        _onComplete = onComplete;
    }

    public async Task RunAsync()
    {
        await Task.Delay(1500);
        await _onComplete();
    }
}
