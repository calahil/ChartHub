using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using ChartHub.Models;
using ChartHub.ViewModels;

namespace ChartHub.Views;

public partial class RhythmVerseView : UserControl
{
    private RhythmVerseViewModel? _subscribedViewModel;
    private ScrollViewer? _desktopScrollViewer;
    private ScrollViewer? _mobileScrollViewer;

    public RhythmVerseView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        ScheduleAttachScrollHandlers();
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

        ScheduleAttachScrollHandlers();
    }

    private void ScheduleAttachScrollHandlers()
    {
        Dispatcher.UIThread.Post(AttachScrollHandlers, DispatcherPriority.Loaded);
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

    private void AttachScrollHandlers()
    {
        if (_desktopScrollViewer is not null)
        {
            _desktopScrollViewer.ScrollChanged -= SongsList_ScrollChanged;
        }

        if (_mobileScrollViewer is not null)
        {
            _mobileScrollViewer.ScrollChanged -= SongsList_ScrollChanged;
        }

        _desktopScrollViewer = SongsListBox?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        _mobileScrollViewer = MobileSongsListBox?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

        if (_desktopScrollViewer is not null)
        {
            _desktopScrollViewer.ScrollChanged += SongsList_ScrollChanged;
        }

        if (_mobileScrollViewer is not null)
        {
            _mobileScrollViewer.ScrollChanged += SongsList_ScrollChanged;
        }
    }

    private async void SongsList_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (DataContext is not RhythmVerseViewModel viewModel)
        {
            return;
        }

        var scrollViewer = sender as ScrollViewer;
        if (scrollViewer is null)
        {
            return;
        }

        double remaining = scrollViewer.Extent.Height - scrollViewer.Viewport.Height - scrollViewer.Offset.Y;
        if (remaining > 200)
        {
            return;
        }

        await viewModel.LoadMoreAsync();
    }

    private async void SearchTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not RhythmVerseViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        if (viewModel.RefreshButtonCommand.CanExecute(null))
        {
            await viewModel.RefreshButtonCommand.ExecuteAsync(null);
        }
    }
}
