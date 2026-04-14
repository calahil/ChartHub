using System.Collections.Generic;
using System.Windows.Input;

using Avalonia;
using Avalonia.Controls;

using ChartHub.Localization;
using ChartHub.Services;

namespace ChartHub.Views;

public partial class SharedDownloadCardsView : UserControl
{
    public static readonly StyledProperty<IEnumerable<DownloadFile>?> DownloadsProperty =
        AvaloniaProperty.Register<SharedDownloadCardsView, IEnumerable<DownloadFile>?>(nameof(Downloads));

    public static readonly StyledProperty<ICommand?> CancelCommandProperty =
        AvaloniaProperty.Register<SharedDownloadCardsView, ICommand?>(nameof(CancelCommand));

    public static readonly StyledProperty<ICommand?> ClearCommandProperty =
        AvaloniaProperty.Register<SharedDownloadCardsView, ICommand?>(nameof(ClearCommand));

    public static readonly StyledProperty<bool> HasActiveDownloadsProperty =
        AvaloniaProperty.Register<SharedDownloadCardsView, bool>(nameof(HasActiveDownloads));

    public static readonly StyledProperty<bool> IsCompactProperty =
        AvaloniaProperty.Register<SharedDownloadCardsView, bool>(nameof(IsCompact));

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<SharedDownloadCardsView, string>(nameof(Title), UiLocalization.Get("Downloads.ActiveDownloads"));

    static SharedDownloadCardsView()
    {
        IsCompactProperty.Changed.AddClassHandler<SharedDownloadCardsView>((view, args) =>
        {
            bool isCompact = args.NewValue is bool value && value;
            view.Classes.Set("compact", isCompact);
        });
    }

    public SharedDownloadCardsView()
    {
        InitializeComponent();
    }

    public IEnumerable<DownloadFile>? Downloads
    {
        get => GetValue(DownloadsProperty);
        set => SetValue(DownloadsProperty, value);
    }

    public ICommand? CancelCommand
    {
        get => GetValue(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }

    public ICommand? ClearCommand
    {
        get => GetValue(ClearCommandProperty);
        set => SetValue(ClearCommandProperty, value);
    }

    public bool HasActiveDownloads
    {
        get => GetValue(HasActiveDownloadsProperty);
        set => SetValue(HasActiveDownloadsProperty, value);
    }

    public bool IsCompact
    {
        get => GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }
}
