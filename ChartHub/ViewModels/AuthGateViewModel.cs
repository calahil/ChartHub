using CommunityToolkit.Mvvm.Input;
using ChartHub.Services;
using ChartHub.Utilities;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ChartHub.ViewModels;

public class AuthGateViewModel : INotifyPropertyChanged
{
    private readonly IGoogleDriveClient _googleDriveClient;
    private readonly Func<Task> _onAuthenticatedAsync;

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value)
                return;

            _isBusy = value;
            OnPropertyChanged();
            SignInCommand.NotifyCanExecuteChanged();
        }
    }

    private string _statusMessage = "Sign in to Google Drive to enable synced storage.";
    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            _errorMessage = value;
            OnPropertyChanged();
        }
    }

    public IAsyncRelayCommand SignInCommand { get; }

    public AuthGateViewModel(IGoogleDriveClient googleDriveClient, Func<Task> onAuthenticatedAsync)
    {
        _googleDriveClient = googleDriveClient;
        _onAuthenticatedAsync = onAuthenticatedAsync;
        SignInCommand = new AsyncRelayCommand(SignInAsync, CanSignIn);
    }

    private bool CanSignIn() => !IsBusy;

    private async Task SignInAsync()
    {
        if (IsBusy)
            return;

        var authSessionId = $"auth-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = "Opening Google sign-in...";
        Logger.LogInfo("Auth", "Interactive Google Drive sign-in started", new Dictionary<string, object?>
        {
            ["authSessionId"] = authSessionId,
        });

        try
        {
            await _googleDriveClient.InitializeAsync();
            Logger.LogInfo("Auth", "Google Drive sign-in completed successfully", new Dictionary<string, object?>
            {
                ["authSessionId"] = authSessionId,
            });
            StatusMessage = "Google Drive connected.";
            await _onAuthenticatedAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError("Auth", "Google authentication failed", ex, new Dictionary<string, object?>
            {
                ["authSessionId"] = authSessionId,
            });
            ErrorMessage = ex.Message.StartsWith("Google sign-in failed", StringComparison.OrdinalIgnoreCase)
                ? ex.Message
                : $"Google sign-in failed: {ex.Message}";
            StatusMessage = "Sign in to Google Drive to enable synced storage.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
