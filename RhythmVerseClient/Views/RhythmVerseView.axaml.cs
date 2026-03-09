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

    // private void Button_Click(object sender, RoutedEventArgs e)
    // {
    //     if (DataContext is RhythmVerseViewModel viewModel)
    //     {
    //         viewModel.DownloadFileCommand.Execute(null);
    //     }
    // }
    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Handle any UI updates needed when view model properties change
    }
}
