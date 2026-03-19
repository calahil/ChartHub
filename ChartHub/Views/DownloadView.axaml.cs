using Avalonia;
using Avalonia.Controls;
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
        });
    }
}
