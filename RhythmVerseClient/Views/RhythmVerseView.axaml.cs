using Avalonia.Controls;
using RhythmVerseClient.ViewModels;

namespace RhythmVerseClient.Views;

public partial class RhythmVerseView : UserControl
{
    public RhythmVerseView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        
        // Subscribe to ViewModel property changes if needed
        if (DataContext is RhythmVerseModel viewModel)
        {
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Handle any UI updates needed when view model properties change
    }
}
