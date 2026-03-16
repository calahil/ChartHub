using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using RhythmVerseClient.Models;
using RhythmVerseClient.ViewModels;

namespace RhythmVerseClient.Views;

public partial class RhythmVerseView : UserControl
{
    private RhythmVerseViewModel? _subscribedViewModel;

    public RhythmVerseView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _subscribedViewModel = null;
        }

        if (DataContext is RhythmVerseViewModel viewModel)
        {
            _subscribedViewModel = viewModel;
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Handle any UI updates needed when view model properties change
    }

    private void Button_Spin(object? sender, SpinEventArgs e)
    {
        if (DataContext is RhythmVerseViewModel viewModel)
        {
            if (e.Direction == SpinDirection.Increase)
            {
                viewModel.CurrentPage++;
            }
            else if (e.Direction == SpinDirection.Decrease)
            {
                viewModel.CurrentPage--;
            }
        }
    }
}
