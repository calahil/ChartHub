using Avalonia;
using Avalonia.Controls;
using ChartHub.ViewModels;

namespace ChartHub.Views;

public partial class DownloadView : UserControl
{
    public DownloadView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        // DataContext is now set to DownloadViewModel
    }
}
