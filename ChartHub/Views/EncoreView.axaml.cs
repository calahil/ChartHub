using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

using ChartHub.ViewModels;

namespace ChartHub.Views;

public partial class EncoreView : UserControl
{
    private ScrollViewer? _desktopScrollViewer;
    private ScrollViewer? _mobileScrollViewer;

    public EncoreView()
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
        ScheduleAttachScrollHandlers();
    }

    private void ScheduleAttachScrollHandlers()
    {
        Dispatcher.UIThread.Post(AttachScrollHandlers, DispatcherPriority.Loaded);
    }

    private void AttachScrollHandlers()
    {
        if (_desktopScrollViewer is not null)
        {
            _desktopScrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;
        }

        if (_mobileScrollViewer is not null)
        {
            _mobileScrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;
        }

        _desktopScrollViewer = EncoreSongsListBox?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        _mobileScrollViewer = MobileSongsListBox?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

        if (_desktopScrollViewer is not null)
        {
            _desktopScrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
        }

        if (_mobileScrollViewer is not null)
        {
            _mobileScrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
        }
    }

    private async void ScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (DataContext is not EncoreViewModel viewModel || sender is not ScrollViewer scrollViewer)
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
        if (e.Key != Key.Enter || DataContext is not EncoreViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        if (viewModel.RefreshCommand.CanExecute(null))
        {
            await viewModel.RefreshCommand.ExecuteAsync(null);
        }
    }
}