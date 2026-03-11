using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using RhythmVerseClient.Models;
using RhythmVerseClient.Strings;
using System.Collections.Generic;

namespace RhythmVerseClient.Controls;
public partial class SongInfoControl : UserControl
{
    /// <summary>The ViewSong Model being passed to the control.</summary>
    public static readonly StyledProperty<ViewSong> SongProperty =
        AvaloniaProperty.Register<SongInfoControl, ViewSong>(
            nameof(Song),
            defaultValue: null);

    public static readonly StyledProperty<RhythmVersePageStrings> SongStringsProperty =
        AvaloniaProperty.Register<SongInfoControl, RhythmVersePageStrings>(
            nameof(SongStrings),
            defaultValue: null);

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

    public SongInfoControl()
    {
        InitializeComponent();

    }
}