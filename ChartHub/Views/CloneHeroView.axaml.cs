using Avalonia;
using Avalonia.Controls;

using ChartHub.ViewModels;

namespace ChartHub.Views;

public partial class CloneHeroView : UserControl
{
    public CloneHeroView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        // DataContext is now set to CloneHeroViewModel
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // View is now attached to the visual tree, you can perform any additional setup here
        if (this.DataContext is CloneHeroViewModel viewModel)
        {
        }
    }
}
