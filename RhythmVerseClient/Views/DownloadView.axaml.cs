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
}
