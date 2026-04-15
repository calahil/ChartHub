namespace ChartHub.Services;

public sealed class StatusBarService : IStatusBarService
{
    private string _currentMessage = string.Empty;

    public string CurrentMessage
    {
        get => _currentMessage;
        private set
        {
            _currentMessage = value;
            MessageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? MessageChanged;

    public void Post(string message)
    {
        CurrentMessage = message ?? string.Empty;
    }

    public void Clear()
    {
        CurrentMessage = string.Empty;
    }
}
