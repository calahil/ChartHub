using CommunityToolkit.Mvvm.Input;
using ChartHub.Services;
using ChartHub.Utilities;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ChartHub.ViewModels;

public class AuthGateViewModel : INotifyPropertyChanged
{
    private readonly ICloudStorageAccountService _cloudAccountService;
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

    private string _statusMessage;
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

    public string ProviderDisplayName => _cloudAccountService.ProviderDisplayName;

    public string DescriptionText => $"Sign in with {ProviderDisplayName} to use cloud storage as your sync backend.";

    public string SignInButtonText => $"Sign In With {ProviderDisplayName}";

    public AuthGateViewModel(ICloudStorageAccountService cloudAccountService, Func<Task> onAuthenticatedAsync)
    {
        _cloudAccountService = cloudAccountService;
        _onAuthenticatedAsync = onAuthenticatedAsync;
        _statusMessage = $"Sign in to {ProviderDisplayName} to enable synced storage.";
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
        StatusMessage = $"Opening {ProviderDisplayName} sign-in...";
        Logger.LogInfo("Auth", "Interactive cloud sign-in started", new Dictionary<string, object?>
        {
            ["authSessionId"] = authSessionId,
            ["providerId"] = _cloudAccountService.ProviderId,
        });

        try
        {
            await _cloudAccountService.LinkAsync();
            Logger.LogInfo("Auth", "Cloud sign-in completed successfully", new Dictionary<string, object?>
            {
                ["authSessionId"] = authSessionId,
                ["providerId"] = _cloudAccountService.ProviderId,
            });
            StatusMessage = $"{ProviderDisplayName} connected.";
            await _onAuthenticatedAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError("Auth", "Cloud authentication failed", ex, new Dictionary<string, object?>
            {
                ["authSessionId"] = authSessionId,
                ["providerId"] = _cloudAccountService.ProviderId,
            });
            ErrorMessage = ex.Message.StartsWith("Cloud sign-in failed", StringComparison.OrdinalIgnoreCase)
                ? ex.Message
                : $"Cloud sign-in failed: {ex.Message}";
            StatusMessage = $"Sign in to {ProviderDisplayName} to enable synced storage.";
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
