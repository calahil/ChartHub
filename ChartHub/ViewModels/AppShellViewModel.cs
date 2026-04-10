using System.ComponentModel;
using System.Runtime.CompilerServices;

using Avalonia;

using Microsoft.Extensions.DependencyInjection;

namespace ChartHub.ViewModels;

public class AppShellViewModel : INotifyPropertyChanged
{
    private readonly IServiceProvider _serviceProvider;
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

    public Thickness RootMargin => OperatingSystem.IsAndroid() ? new Thickness(0, 32, 0, 0) : new Thickness(0);

    public AppShellViewModel(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        CurrentViewModel = new SplashViewModel(HandlePostSplashAsync);
    }

    private Task HandlePostSplashAsync()
    {
        SwitchToMain();
        return Task.CompletedTask;
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
