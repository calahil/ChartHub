using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

using ChartHub.ViewModels;

namespace ChartHub.Views;

public partial class EncoreView : UserControl
{
    private const int LoadMoreDebounceMs = 120;

    private ScrollViewer? _desktopScrollViewer;
    private ScrollViewer? _mobileScrollViewer;
    private CancellationTokenSource? _loadMoreDebounceCts;
    private int _loadMoreInFlight;

    public EncoreView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ScheduleAttachScrollHandlers();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        _loadMoreDebounceCts?.Cancel();
        _loadMoreDebounceCts?.Dispose();
        _loadMoreDebounceCts = null;
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

        if (Interlocked.CompareExchange(ref _loadMoreInFlight, 1, 0) != 0)
        {
            return;
        }

        _loadMoreDebounceCts?.Cancel();
        _loadMoreDebounceCts?.Dispose();
        _loadMoreDebounceCts = new CancellationTokenSource();
        CancellationToken cancellationToken = _loadMoreDebounceCts.Token;

        try
        {
            // Debounce and yield once so pagination does not execute inside the active offset-change callback.
            await Task.Delay(LoadMoreDebounceMs, cancellationToken);
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
            cancellationToken.ThrowIfCancellationRequested();

            // Fill the viewport: keep loading pages until there is enough content to scroll or records are exhausted.
            while (viewModel.HasMoreRecords)
            {
                await viewModel.LoadMoreAsync();
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
                double postRemaining = scrollViewer.Extent.Height - scrollViewer.Viewport.Height - scrollViewer.Offset.Y;
                if (postRemaining > 200)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // A newer scroll event superseded this scheduled pagination attempt.
        }
        finally
        {
            Interlocked.Exchange(ref _loadMoreInFlight, 0);
        }
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