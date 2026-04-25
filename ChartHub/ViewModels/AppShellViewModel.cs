using System.ComponentModel;
using System.Runtime.CompilerServices;

using Avalonia;

using ChartHub.Services;

using Microsoft.Extensions.DependencyInjection;

namespace ChartHub.ViewModels;

public class AppShellViewModel : INotifyPropertyChanged
{
    private readonly IServiceProvider _serviceProvider;
    private MainViewModel? _mainViewModel;
    private InputShellViewModel? _inputShellViewModel;

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
        CurrentViewModel = new SplashViewModel(
            _serviceProvider.GetRequiredService<IAuthSessionService>(),
            HandlePostSplashAsync);
    }

    private Task HandlePostSplashAsync()
    {
        SwitchToMain();
        return Task.CompletedTask;
    }

    private void SwitchToMain()
    {
        if (_mainViewModel is null)
        {
            _mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
            _mainViewModel.InputRequested += HandleInputRequested;
        }

        CurrentViewModel = _mainViewModel;
        OnPropertyChanged(nameof(RootMargin));
    }

    private void HandleInputRequested(object? sender, EventArgs e)
    {
        if (_inputShellViewModel is null)
        {
            _inputShellViewModel = _serviceProvider.GetRequiredService<InputShellViewModel>();
            _inputShellViewModel.BackRequested += HandleInputBackRequested;
        }

        _inputShellViewModel.ResetToController();
        CurrentViewModel = _inputShellViewModel;
    }

    private void HandleInputBackRequested(object? sender, EventArgs e)
    {
        _mainViewModel?.GoDesktopEntryCommand.Execute(null);
        CurrentViewModel = _mainViewModel;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
