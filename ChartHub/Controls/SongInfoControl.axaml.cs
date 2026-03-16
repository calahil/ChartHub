using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ChartHub.Models;
using ChartHub.Strings;
using System.Collections.Generic;

namespace ChartHub.Controls;
public partial class SongInfoControl : UserControl
{
    /// <summary>The ViewSong Model being passed to the control.</summary>
    public static readonly StyledProperty<ViewSong> SongProperty =
        AvaloniaProperty.Register<SongInfoControl, ViewSong>(
            nameof(Song),
            defaultValue: new ViewSong());

    public static readonly StyledProperty<RhythmVersePageStrings> SongStringsProperty =
        AvaloniaProperty.Register<SongInfoControl, RhythmVersePageStrings>(
            nameof(SongStrings),
            defaultValue: new RhythmVersePageStrings());

    public static readonly StyledProperty<bool> IsDesktopModeProperty =
        AvaloniaProperty.Register<SongInfoControl, bool>(
            nameof(IsDesktopMode),
            defaultValue: !OperatingSystem.IsAndroid());

    public static readonly StyledProperty<bool> IsCompanionModeProperty =
        AvaloniaProperty.Register<SongInfoControl, bool>(
            nameof(IsCompanionMode),
            defaultValue: OperatingSystem.IsAndroid());

    public ViewSong Song
    {
        get => (ViewSong)GetValue(SongProperty);
        set => SetValue(SongProperty, value);
    }

    public RhythmVersePageStrings SongStrings
    {
        get => (RhythmVersePageStrings)GetValue(SongStringsProperty);
        set => SetValue(SongStringsProperty, value);
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

    public SongInfoControl()
    {
        InitializeComponent();
    }
}