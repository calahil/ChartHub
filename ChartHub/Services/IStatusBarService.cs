namespace ChartHub.Services;

public interface IStatusBarService
{
    string CurrentMessage { get; }

    void Post(string message);

    void Clear();

    event EventHandler? MessageChanged;
}
