using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

using ChartHub.Models;
using ChartHub.Strings;

namespace ChartHub.Controls;

public partial class SongInfoControl : UserControl
{
    /// <summary>Song model passed to the control (supports ViewSong or EncoreSong).</summary>
    public static readonly StyledProperty<object?> SongProperty =
        AvaloniaProperty.Register<SongInfoControl, object?>(
            nameof(Song),
            defaultValue: null);

    public static readonly StyledProperty<SongSourcePageStrings> SongStringsProperty =
        AvaloniaProperty.Register<SongInfoControl, SongSourcePageStrings>(
            nameof(SongStrings),
            defaultValue: new SongSourcePageStrings());

    public static readonly StyledProperty<string> ArtistTextProperty =
        AvaloniaProperty.Register<SongInfoControl, string>(nameof(ArtistText), string.Empty);

    public static readonly StyledProperty<string> TitleTextProperty =
        AvaloniaProperty.Register<SongInfoControl, string>(nameof(TitleText), string.Empty);

    public static readonly StyledProperty<string> AlbumTextProperty =
        AvaloniaProperty.Register<SongInfoControl, string>(nameof(AlbumText), string.Empty);

    public static readonly StyledProperty<string> FormattedTimeTextProperty =
        AvaloniaProperty.Register<SongInfoControl, string>(nameof(FormattedTimeText), string.Empty);

    public static readonly StyledProperty<string> YearTextProperty =
        AvaloniaProperty.Register<SongInfoControl, string>(nameof(YearText), string.Empty);

    public static readonly StyledProperty<string> GenreTextProperty =
        AvaloniaProperty.Register<SongInfoControl, string>(nameof(GenreText), string.Empty);

    public static readonly StyledProperty<bool> IsDesktopModeProperty =
        AvaloniaProperty.Register<SongInfoControl, bool>(
            nameof(IsDesktopMode),
            defaultValue: !OperatingSystem.IsAndroid());

    public static readonly StyledProperty<bool> IsCompanionModeProperty =
        AvaloniaProperty.Register<SongInfoControl, bool>(
            nameof(IsCompanionMode),
            defaultValue: OperatingSystem.IsAndroid());

    private INotifyPropertyChanged? _songNotifier;

    public object? Song
    {
        get => GetValue(SongProperty);
        set => SetValue(SongProperty, value);
    }

    public SongSourcePageStrings SongStrings
    {
        get => GetValue(SongStringsProperty);
        set => SetValue(SongStringsProperty, value);
    }

    public string ArtistText
    {
        get => GetValue(ArtistTextProperty);
        private set => SetValue(ArtistTextProperty, value);
    }

    public string TitleText
    {
        get => GetValue(TitleTextProperty);
        private set => SetValue(TitleTextProperty, value);
    }

    public string AlbumText
    {
        get => GetValue(AlbumTextProperty);
        private set => SetValue(AlbumTextProperty, value);
    }

    public string FormattedTimeText
    {
        get => GetValue(FormattedTimeTextProperty);
        private set => SetValue(FormattedTimeTextProperty, value);
    }

    public string YearText
    {
        get => GetValue(YearTextProperty);
        private set => SetValue(YearTextProperty, value);
    }

    public string GenreText
    {
        get => GetValue(GenreTextProperty);
        private set => SetValue(GenreTextProperty, value);
    }

    public bool IsDesktopMode
    {
        get => GetValue(IsDesktopModeProperty);
        set => SetValue(IsDesktopModeProperty, value);
    }

    public bool IsCompanionMode
    {
        get => GetValue(IsCompanionModeProperty);
        set => SetValue(IsCompanionModeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SongProperty)
        {
            AttachSongNotifier(change.NewValue);
            RefreshSongInfo();
        }
    }

    private void AttachSongNotifier(object? model)
    {
        if (_songNotifier is not null)
        {
            _songNotifier.PropertyChanged -= SongNotifier_PropertyChanged;
            _songNotifier = null;
        }

        if (model is INotifyPropertyChanged notifier)
        {
            _songNotifier = notifier;
            _songNotifier.PropertyChanged += SongNotifier_PropertyChanged;
        }
    }

    private void SongNotifier_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshSongInfo();
            return;
        }

        Dispatcher.UIThread.Post(RefreshSongInfo);
    }

    private void RefreshSongInfo()
    {
        if (Song is EncoreSong encoreSong)
        {
            ArtistText = encoreSong.Artist;
            TitleText = encoreSong.Name;
            AlbumText = encoreSong.Album;
            FormattedTimeText = encoreSong.FormattedTime;
            YearText = encoreSong.Year;
            GenreText = encoreSong.Genre;
            return;
        }

        if (Song is ViewSong viewSong)
        {
            ArtistText = viewSong.Artist ?? string.Empty;
            TitleText = viewSong.Title ?? string.Empty;
            AlbumText = viewSong.Album ?? string.Empty;
            FormattedTimeText = viewSong.FormattedTime ?? string.Empty;
            YearText = viewSong.Year ?? string.Empty;
            GenreText = viewSong.Genre ?? string.Empty;
            return;
        }

        ArtistText = string.Empty;
        TitleText = string.Empty;
        AlbumText = string.Empty;
        FormattedTimeText = string.Empty;
        YearText = string.Empty;
        GenreText = string.Empty;
    }

    public SongInfoControl()
    {
        InitializeComponent();
        RefreshSongInfo();
    }
}