using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using ChartHub.Strings;

using CommunityToolkit.Mvvm.Input;

namespace ChartHub.ViewModels;

public class InputShellViewModel : INotifyPropertyChanged
{
    private readonly VirtualControllerViewModel _virtualControllerViewModel;
    private readonly VirtualTouchPadViewModel _virtualTouchPadViewModel;
    private readonly VirtualKeyboardViewModel _virtualKeyboardViewModel;
    private readonly InputShellPageStrings _pageStrings = new InputShellPageStrings();

    private object? _currentInputViewModel;
    private bool _isNavPaneOpen;

    public object? CurrentInputViewModel
    {
        get => _currentInputViewModel;
        private set
        {
            _currentInputViewModel = value;
            OnPropertyChanged();
        }
    }

    public bool IsNavPaneOpen
    {
        get => _isNavPaneOpen;
        set
        {
            _isNavPaneOpen = value;
            OnPropertyChanged();
        }
    }

    public InputShellPageStrings PageStrings => _pageStrings;

    public IRelayCommand ToggleNavPaneCommand { get; }
    public IRelayCommand GoControllerCommand { get; }
    public IRelayCommand GoMouseCommand { get; }
    public IRelayCommand GoKeyboardCommand { get; }
    public IRelayCommand GoBackCommand { get; }

    public event EventHandler? BackRequested;

    public InputShellViewModel(
        VirtualControllerViewModel virtualControllerViewModel,
        VirtualTouchPadViewModel virtualTouchPadViewModel,
        VirtualKeyboardViewModel virtualKeyboardViewModel)
    {
        _virtualControllerViewModel = virtualControllerViewModel;
        _virtualTouchPadViewModel = virtualTouchPadViewModel;
        _virtualKeyboardViewModel = virtualKeyboardViewModel;

        _currentInputViewModel = _virtualControllerViewModel;

        ToggleNavPaneCommand = new RelayCommand(() => IsNavPaneOpen = !IsNavPaneOpen);
        GoControllerCommand = new RelayCommand(() => NavigateTo(_virtualControllerViewModel));
        GoMouseCommand = new RelayCommand(() => NavigateTo(_virtualTouchPadViewModel));
        GoKeyboardCommand = new RelayCommand(() => NavigateTo(_virtualKeyboardViewModel));
        GoBackCommand = new RelayCommand(OnGoBack);
    }

    public void ResetToController()
    {
        _currentInputViewModel = _virtualControllerViewModel;
        OnPropertyChanged(nameof(CurrentInputViewModel));
        IsNavPaneOpen = false;
    }

    private void NavigateTo(object viewModel)
    {
        CurrentInputViewModel = viewModel;
        IsNavPaneOpen = false;
    }

    private void OnGoBack()
    {
        IsNavPaneOpen = false;
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
