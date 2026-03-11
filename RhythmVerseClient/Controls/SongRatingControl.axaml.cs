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
            defaultValue: null);

    public ViewSong Song
    {
        get => (ViewSong)GetValue(SongProperty);
        set => SetValue(SongProperty, value);
    }

    public SongRatingControl()
    {
        InitializeComponent();
    }
}