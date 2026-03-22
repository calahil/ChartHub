using Avalonia;
using Avalonia.Controls;

using ChartHub.ViewModels;

namespace ChartHub.Views;

public partial class SplashView : UserControl
{
    public SplashView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (DataContext is SplashViewModel vm)
        {
            _ = vm.RunAsync();
        }
    }
}
