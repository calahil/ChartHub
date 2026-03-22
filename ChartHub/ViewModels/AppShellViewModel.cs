using System.ComponentModel;
using System.Runtime.CompilerServices;

using Avalonia;

using ChartHub.Services;

using Microsoft.Extensions.DependencyInjection;

namespace ChartHub.ViewModels;

public class AppShellViewModel : INotifyPropertyChanged
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ICloudStorageAccountService _cloudAccountService;
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

    public Thickness RootMargin => OperatingSystem.IsAndroid() ? new Thickness(0, 32, 0, 0) : new Thickness(0);

    public AppShellViewModel(IServiceProvider serviceProvider, ICloudStorageAccountService cloudAccountService)
    {
        _serviceProvider = serviceProvider;
        _cloudAccountService = cloudAccountService;
        CurrentViewModel = new SplashViewModel(HandlePostSplashAsync);
    }

    private async Task HandlePostSplashAsync()
    {
        bool silentlyInitialized = await _cloudAccountService.TryRestoreSessionAsync();
        IsSignedIn = silentlyInitialized;
        SwitchToMain();
    }

    private void SwitchToMain()
    {
        _mainViewModel ??= _serviceProvider.GetRequiredService<MainViewModel>();
        CurrentViewModel = _mainViewModel;
        OnPropertyChanged(nameof(RootMargin));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
