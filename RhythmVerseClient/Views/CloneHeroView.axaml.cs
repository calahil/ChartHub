using Avalonia.Controls;

namespace RhythmVerseClient.Views;

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
}
