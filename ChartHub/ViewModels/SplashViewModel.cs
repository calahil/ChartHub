using ChartHub.Services;

namespace ChartHub.ViewModels;

public class SplashViewModel
{
    private readonly IAuthSessionService _authSessionService;
    private readonly Func<Task> _onComplete;

    public SplashViewModel(IAuthSessionService authSessionService, Func<Task> onComplete)
    {
        _authSessionService = authSessionService;
        _onComplete = onComplete;
    }

    public async Task RunAsync()
    {
        // Attempt silent auth restore (non-blocking on failure)
        await _authSessionService.AttemptSilentRestoreAsync().ConfigureAwait(false);

        // Brief delay for visual splash (1.5s gives time for cache load to complete)
        await Task.Delay(1500).ConfigureAwait(false);

        // Transition to main shell (auth gate overlay will be shown if needed)
        await _onComplete().ConfigureAwait(false);
    }
}
