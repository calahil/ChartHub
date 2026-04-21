using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using ChartHub.Hud.ViewModels;

namespace ChartHub.Hud.Views;

public partial class HudWindow : Avalonia.Controls.Window
{
    public HudWindow()
    {
        AvaloniaXamlLoader.Load(this);

        Opened += OnOpened;
        SizeChanged += OnSizeChanged;
        Closed += OnClosed;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        PublishViewport();
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        PublishViewport();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        SizeChanged -= OnSizeChanged;
        Closed -= OnClosed;
    }

    private void PublishViewport()
    {
        if (DataContext is HudViewModel vm)
        {
            vm.UpdateViewport(Bounds.Width, Bounds.Height);
        }
    }
}
