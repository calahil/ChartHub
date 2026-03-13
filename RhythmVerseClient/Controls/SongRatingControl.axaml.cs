using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using RhythmVerseClient.Models;
using RhythmVerseClient.Strings;
using System.Collections.Generic;

namespace RhythmVerseClient.Controls;
public partial class SongRatingControl : UserControl
{
    /// <summary>The ViewSong Model being passed to the control.</summary>
    public static readonly StyledProperty<ViewSong> SongProperty =
        AvaloniaProperty.Register<SongRatingControl, ViewSong>(
            nameof(Song),
            defaultValue: new ViewSong());

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

     public static readonly StyledProperty<FontFamily> GlyphFontFamilyProperty =
        AvaloniaProperty.Register<SongRatingControl, FontFamily>(
            nameof(GlyphFontFamily),
            defaultValue: new FontFamily("avares://RhythmVerseClient/Resources/Fonts/CaskaydiaCoveNerdFont-Regular.ttf#CaskaydiaCove NF"));

     public static readonly StyledProperty<bool> IsDesktopModeProperty =
        AvaloniaProperty.Register<SongRatingControl, bool>(
            nameof(IsDesktopMode),
            defaultValue: !OperatingSystem.IsAndroid());

    public static readonly StyledProperty<bool> IsCompanionModeProperty =
        AvaloniaProperty.Register<SongRatingControl, bool>(
            nameof(IsCompanionMode),
            defaultValue: OperatingSystem.IsAndroid());

    public ViewSong Song
    {
        get => (ViewSong)GetValue(SongProperty);
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

    public FontFamily GlyphFontFamily
    {
        get => GetValue(GlyphFontFamilyProperty);
        set => SetValue(GlyphFontFamilyProperty, value);
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

    // ── Property change handling ──────────────────────────────────────────
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
    }

    public SongRatingControl()
    {
        InitializeComponent();
    }
}