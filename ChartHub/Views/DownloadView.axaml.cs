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

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
    }
}

