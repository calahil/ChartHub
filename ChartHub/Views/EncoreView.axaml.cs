using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ChartHub.ViewModels;

namespace ChartHub.Views;

public partial class EncoreView : UserControl
{
    private ScrollViewer? _scrollViewer;

    public EncoreView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachScrollHandler();
    }

    private void AttachScrollHandler()
    {
        if (_scrollViewer is not null)
            _scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;

        _scrollViewer = EncoreSongsListBox?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_scrollViewer is not null)
            _scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
    }

    private async void ScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (DataContext is not EncoreViewModel viewModel || sender is not ScrollViewer scrollViewer)
            return;

        var remaining = scrollViewer.Extent.Height - scrollViewer.Viewport.Height - scrollViewer.Offset.Y;
        if (remaining > 200)
            return;

        await viewModel.LoadMoreAsync();
    }
}