using Avalonia;
using Avalonia.Controls;
using RhythmVerseClient.ViewModels;

namespace RhythmVerseClient.Views;

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

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // View is now attached to the visual tree, you can perform any additional setup here
        var viewModel = this.DataContext as DownloadViewModel;
        if (viewModel != null)
        {
            // You can access the ViewModel properties and methods here
            viewModel.DownloadWatcher.LoadItems();
        }
    }
}
