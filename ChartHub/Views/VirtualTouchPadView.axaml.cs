using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

using ChartHub.ViewModels;

namespace ChartHub.Views;

public partial class VirtualTouchPadView : UserControl
{
    private bool _isTracking;
    private Avalonia.Point _lastPosition;

    public VirtualTouchPadView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is VirtualTouchPadViewModel vm)
            {
                vm.Activate();
            }
        };
        Unloaded += (_, _) =>
        {
            if (DataContext is VirtualTouchPadViewModel vm)
            {
                vm.Deactivate();
            }
        };
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnTouchSurfacePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border)
        {
            return;
        }

        _isTracking = true;
        _lastPosition = e.GetPosition(border);
        e.Pointer.Capture(border);
        e.Handled = true;
    }

    private void OnTouchSurfacePointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isTracking || sender is not Border border)
        {
            return;
        }

        Avalonia.Point current = e.GetPosition(border);
        int dx = (int)(current.X - _lastPosition.X);
        int dy = (int)(current.Y - _lastPosition.Y);
        _lastPosition = current;

        if (dx != 0 || dy != 0)
        {
            if (DataContext is VirtualTouchPadViewModel vm)
            {
                vm.OnTouchPadDelta(dx, dy);
            }
        }

        e.Handled = true;
    }

    private void OnTouchSurfacePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isTracking = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnTouchSurfaceCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isTracking = false;
    }

    private void OnMouseButtonPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { Tag: string side } && DataContext is VirtualTouchPadViewModel vm)
        {
            e.Pointer.Capture((Border)sender);
            vm.PressMouseButtonCommand.Execute(side);
            e.Handled = true;
        }
    }

    private void OnMouseButtonReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Border { Tag: string side } && DataContext is VirtualTouchPadViewModel vm)
        {
            e.Pointer.Capture(null);
            vm.ReleaseMouseButtonCommand.Execute(side);
            e.Handled = true;
        }
    }
}
