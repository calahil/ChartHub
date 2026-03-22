using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;

using ChartHub.ViewModels;

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

        DetachFromViewModel();

        _viewModel = DataContext as DownloadViewModel;
        AttachToViewModel();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DetachFromViewModel();

        base.OnDetachedFromVisualTree(e);
    }

    private void AttachToViewModel()
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.InstallLogItems.CollectionChanged += InstallLogItems_CollectionChanged;
    }

    private void DetachFromViewModel()
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.InstallLogItems.CollectionChanged -= InstallLogItems_CollectionChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DownloadViewModel.IsInstallLogExpanded) && _viewModel?.IsInstallLogExpanded == true)
        {
            ScrollLogToBottom();
        }
    }

    private void InstallLogItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Reset)
        {
            ScrollLogToBottom();
        }
    }

    private void ScrollLogToBottom()
    {
        if (_viewModel is not null && !_viewModel.IsInstallLogExpanded)
        {
            return;
        }

        // Post at Background priority so layout has fully updated Extent before we scroll.
        Dispatcher.UIThread.Post(() =>
        {
            if (_viewModel is not null && !_viewModel.IsInstallLogExpanded)
            {
                return;
            }

            ScrollViewer? scrollViewer = this.FindControl<ScrollViewer>("InstallLogScrollViewer");
            if (scrollViewer is null)
            {
                return;
            }

            scrollViewer.ScrollToEnd();
        }, DispatcherPriority.Background);
    }

    private async void CopyLogItem_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem)
        {
            return;
        }

        if (menuItem.DataContext is not string text || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private async void CopyAllLogs_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel is null || _viewModel.InstallLogItems.Count == 0)
        {
            return;
        }

        IEnumerable<string> lines = _viewModel.InstallLogItems.Where(static line => !string.IsNullOrWhiteSpace(line));
        string text = string.Join(Environment.NewLine, lines);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
        }
    }
}
