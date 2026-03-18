using Avalonia;
using Avalonia.Controls;
using ChartHub.Models;
using ChartHub.Strings;
using System.Collections.Generic;

namespace ChartHub.Controls;

public partial class SongRatingControl : UserControl
{
    /// <summary>Unified song model — accepts either ViewSong or EncoreSong.</summary>
    public static readonly StyledProperty<object?> SongProperty =
        AvaloniaProperty.Register<SongRatingControl, object?>(
            nameof(Song),
            defaultValue: null);

    /// <summary>The ViewSong Model being passed to the control.</summary>
    public static readonly StyledProperty<ViewSong> RhythmSongProperty =
        AvaloniaProperty.Register<SongRatingControl, ViewSong>(
            nameof(RhythmSong),
            defaultValue: new ViewSong());

    /// <summary>The EncoreSong Model being passed to the control.</summary>
    public static readonly StyledProperty<EncoreSong> ChorusSongProperty =
        AvaloniaProperty.Register<SongRatingControl, EncoreSong>(
            nameof(ChorusSong),
            defaultValue: new EncoreSong());

    /// <summary>Fixed pixel width of each glyph cell. Defaults to 20.</summary>
    public static readonly StyledProperty<double> GlyphWidthProperty =
        AvaloniaProperty.Register<SongRatingControl, double>(
            nameof(GlyphWidth),
            defaultValue: 22d);

    /// <summary>Font size of each glyph. Defaults to 16.</summary>
    public static readonly StyledProperty<int> GlyphFontSizeProperty =
        AvaloniaProperty.Register<SongRatingControl, int>(
            nameof(GlyphFontSize),
            defaultValue: 16);

    public static readonly StyledProperty<bool> IsDesktopModeProperty =
       AvaloniaProperty.Register<SongRatingControl, bool>(
           nameof(IsDesktopMode),
           defaultValue: !OperatingSystem.IsAndroid());

    public static readonly StyledProperty<bool> IsCompanionModeProperty =
        AvaloniaProperty.Register<SongRatingControl, bool>(
            nameof(IsCompanionMode),
            defaultValue: OperatingSystem.IsAndroid());

    public static readonly StyledProperty<bool> IsEncoreProperty =
       AvaloniaProperty.Register<SongRatingControl, bool>(
           nameof(IsEncore),
           defaultValue: false);

    // Computed per-instrument rating values — populated from Song, RhythmSong, or ChorusSong.
    public static readonly StyledProperty<int> DrumStringProperty =
        AvaloniaProperty.Register<SongRatingControl, int>(nameof(DrumString), defaultValue: 0);
    public static readonly StyledProperty<int> GuitarStringProperty =
        AvaloniaProperty.Register<SongRatingControl, int>(nameof(GuitarString), defaultValue: 0);
    public static readonly StyledProperty<int> BassStringProperty =
        AvaloniaProperty.Register<SongRatingControl, int>(nameof(BassString), defaultValue: 0);
    public static readonly StyledProperty<int> VocalStringProperty =
        AvaloniaProperty.Register<SongRatingControl, int>(nameof(VocalString), defaultValue: 0);
    public static readonly StyledProperty<int> KeysStringProperty =
        AvaloniaProperty.Register<SongRatingControl, int>(nameof(KeysString), defaultValue: 0);

    public ViewSong RhythmSong
    {
        get => (ViewSong)GetValue(RhythmSongProperty);
        set => SetValue(RhythmSongProperty, value);
    }

    public EncoreSong ChorusSong
    {
        get => (EncoreSong)GetValue(ChorusSongProperty);
        set => SetValue(ChorusSongProperty, value);
    }

    public object? Song
    {
        get => GetValue(SongProperty);
        set => SetValue(SongProperty, value);
    }

    public double GlyphWidth
    {
        get => GetValue(GlyphWidthProperty);
        set => SetValue(GlyphWidthProperty, value);
    }

    public int GlyphFontSize
    {
        get => GetValue(GlyphFontSizeProperty);
        set => SetValue(GlyphFontSizeProperty, value);
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

    public bool IsEncore
    {
        get => GetValue(IsEncoreProperty);
        set => SetValue(IsEncoreProperty, value);
    }

    public int DrumString
    {
        get => GetValue(DrumStringProperty);
        private set => SetValue(DrumStringProperty, value);
    }

    public int GuitarString
    {
        get => GetValue(GuitarStringProperty);
        private set => SetValue(GuitarStringProperty, value);
    }

    public int BassString
    {
        get => GetValue(BassStringProperty);
        private set => SetValue(BassStringProperty, value);
    }

    public int VocalString
    {
        get => GetValue(VocalStringProperty);
        private set => SetValue(VocalStringProperty, value);
    }

    public int KeysString
    {
        get => GetValue(KeysStringProperty);
        private set => SetValue(KeysStringProperty, value);
    }

    // ── Property change handling ──────────────────────────────────────────
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SongProperty ||
            change.Property == RhythmSongProperty ||
            change.Property == ChorusSongProperty)
        {
            RefreshRatings();
        }
    }

    private void RefreshRatings()
    {
        var song = Song;
        if (song is EncoreSong e)
        {
            DrumString = e.DrumsDifficulty ?? 0;
            GuitarString = e.GuitarDifficulty ?? 0;
            BassString = e.BassDifficulty ?? 0;
            VocalString = e.VocalsDifficulty ?? 0;
            KeysString = e.KeysDifficulty ?? 0;
        }
        else if (song is ViewSong sv)
        {
            DrumString = sv.DrumString;
            GuitarString = sv.GuitarString;
            BassString = sv.BassString;
            VocalString = sv.VocalString;
            KeysString = sv.KeysString;
        }
        else
        {
            var rv = RhythmSong;
            DrumString = rv.DrumString;
            GuitarString = rv.GuitarString;
            BassString = rv.BassString;
            VocalString = rv.VocalString;
            KeysString = rv.KeysString;
        }
    }

    public SongRatingControl()
    {
        InitializeComponent();
    }
}