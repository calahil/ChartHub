using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using RhythmVerseClient.Models;
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
        if (DataContext is RhythmVerseViewModel viewModel)
        {
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // View is now attached to the visual tree, you can perform any additional setup here
        if (this.DataContext is RhythmVerseViewModel viewModel)
        {
            // You can access the ViewModel properties and methods here
            _ = viewModel.LoadDataAsync(false);
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Handle any UI updates needed when view model properties change
    }
}
