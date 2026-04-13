using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ChartHub.Views;

public partial class DesktopEntryView : UserControl
{
    public DesktopEntryView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
