using Avalonia.Markup.Xaml;

namespace ChartHub.Hud.Views;

public partial class HudWindow : Avalonia.Controls.Window
{
    public HudWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
