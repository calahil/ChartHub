using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

using ChartHub.ViewModels;

#if ANDROID
using Android.Views;
#endif

namespace ChartHub.Views;

public partial class VirtualControllerView : UserControl
{
    public VirtualControllerView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is VirtualControllerViewModel vm)
            {
                vm.Activate();
            }
        };
        Unloaded += (_, _) =>
        {
            if (DataContext is VirtualControllerViewModel vm)
            {
                vm.Deactivate();
            }
        };
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnControllerButtonPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is global::Avalonia.Controls.Button { Tag: string buttonId } && DataContext is VirtualControllerViewModel vm)
        {
            vm.PressButtonCommand.Execute(buttonId);
            PerformHaptic();
        }
    }

    private void OnControllerButtonReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is global::Avalonia.Controls.Button { Tag: string buttonId } && DataContext is VirtualControllerViewModel vm)
        {
            vm.ReleaseButtonCommand.Execute(buttonId);
        }
    }

    private void OnDPadReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is VirtualControllerViewModel vm)
        {
            vm.SetDPadCommand.Execute("0,0");
        }
    }

    private static void PerformHaptic()
    {
#if ANDROID
        MainActivity? activity = ChartHub.MainActivity.Current;
        // FeedbackConstants.VirtualKey = 1. Use the integer directly to avoid
        // the obsolete HapticFeedbackConstants.VirtualKey constant.
        activity?.Window?.DecorView?.PerformHapticFeedback((Android.Views.FeedbackConstants)1);
#endif
    }
}
