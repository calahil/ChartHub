using CommunityToolkit.Mvvm.Input;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using ChartHub.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ChartHub.ViewModels;

public class AppShellViewModel : INotifyPropertyChanged
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IGoogleDriveClient _googleDriveClient;
    private MainViewModel? _mainViewModel;

    private object? _currentViewModel;
    public object? CurrentViewModel
    {
        get => _currentViewModel;
        private set
        {
            _currentViewModel = value;
            OnPropertyChanged();
        }
    }

    private bool _isSignedIn;
    public bool IsSignedIn
    {
        get => _isSignedIn;
        private set
        {
            _isSignedIn = value;
            OnPropertyChanged();
        }
    }

    public IAsyncRelayCommand SignOutCommand { get; }

    public Thickness RootMargin => OperatingSystem.IsAndroid() ? new Thickness(0, 32, 0, 0) : new Thickness(0);

    public AppShellViewModel(IServiceProvider serviceProvider, IGoogleDriveClient googleDriveClient)
    {
        _serviceProvider = serviceProvider;
        _googleDriveClient = googleDriveClient;
        SignOutCommand = new AsyncRelayCommand(SignOutAsync);
        CurrentViewModel = new SplashViewModel(HandlePostSplashAsync);
    }

    private async Task HandlePostSplashAsync()
    {
        var silentlyInitialized = await _googleDriveClient.TryInitializeSilentAsync();
        if (silentlyInitialized)
        {
            await SwitchToMainAsync();
            return;
        }

        await ShowAuthGateAsync();
    }

    private Task ShowAuthGateAsync()
    {
        CurrentViewModel = new AuthGateViewModel(_googleDriveClient, SwitchToMainAsync);
        return Task.CompletedTask;
    }

    private Task SwitchToMainAsync()
    {
        _mainViewModel ??= _serviceProvider.GetRequiredService<MainViewModel>();
        CurrentViewModel = _mainViewModel;
        IsSignedIn = true;
        OnPropertyChanged(nameof(RootMargin));
        return Task.CompletedTask;
    }

    private async Task SignOutAsync()
    {
        await _googleDriveClient.SignOutAsync();
        _mainViewModel = null;
        IsSignedIn = false;
        CurrentViewModel = new AuthGateViewModel(_googleDriveClient, SwitchToMainAsync);
        OnPropertyChanged(nameof(RootMargin));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
