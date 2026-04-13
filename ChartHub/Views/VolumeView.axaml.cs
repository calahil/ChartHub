using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ChartHub.Views;

public partial class VolumeView : UserControl
{
    public VolumeView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}