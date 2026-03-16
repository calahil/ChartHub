using Avalonia.Controls;

namespace ChartHub.Views;
public partial class InstallSongView : UserControl
{
    public InstallSongView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        // DataContext is now set to InstallSongViewModel
    }
}
