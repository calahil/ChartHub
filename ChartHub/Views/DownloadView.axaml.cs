using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Threading;
using ChartHub.ViewModels;
using System.ComponentModel;

namespace ChartHub.Views;

public partial class DownloadView : UserControl
{
    private DownloadViewModel? _viewModel;

    public DownloadView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;

        _viewModel = DataContext as DownloadViewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;

        base.OnDetachedFromVisualTree(e);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DownloadViewModel.InstallLogText))
            return;

        // Post at Background priority so layout has fully updated Extent before we scroll.
        Dispatcher.UIThread.Post(() =>
        {
            if (_viewModel is not null && !_viewModel.IsInstallLogExpanded)
                return;

            var logTextBox = this.FindControl<TextBox>("InstallLogTextBox");
            if (logTextBox is null)
                return;

            var textLength = logTextBox.Text?.Length ?? 0;
            logTextBox.CaretIndex = textLength;
            logTextBox.SelectionStart = textLength;
            logTextBox.SelectionEnd = textLength;

            // Re-discover the scroll viewer each call to avoid stale references after
            // the TextBox is hidden/shown via IsInstallLogExpanded toggling.
            var scrollViewer = logTextBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (scrollViewer is null)
                return;

            var targetOffsetY = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, targetOffsetY);
        }, DispatcherPriority.Background);
    }
}
