using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System.Collections.Generic;

namespace RhythmVerseClient.Controls;

/// <summary>
/// Displays a 0–5 star rating using uniform fixed-width glyph cells.
/// Each slot is either a filled glyph (≤ Rating) or an empty glyph (> Rating).
/// </summary>
public partial class StarRatingControl : UserControl
{
    // ── Glyphs ────────────────────────────────────────────────────────────
    private const string EmptyGlyph= "\uebb5";
    private const string FilledGlyph  = "\u2B24";
    private const int    TotalStars  = 5;

    // ── Styled Properties ─────────────────────────────────────────────────

    /// <summary>0–5 rating value. Defaults to 0.</summary>
    public static readonly StyledProperty<int> RatingProperty =
        AvaloniaProperty.Register<StarRatingControl, int>(
            nameof(Rating),
            defaultValue: 0,
            coerce: CoerceRating);

    /// <summary>Fixed pixel width of each glyph cell. Defaults to 20.</summary>
    public static readonly StyledProperty<double> GlyphWidthProperty =
        AvaloniaProperty.Register<StarRatingControl, double>(
            nameof(GlyphWidth),
            defaultValue: 20d);

    /// <summary>Brush used for filled stars. Defaults to Yellow.</summary>
    public static readonly StyledProperty<IBrush> FilledBrushProperty =
        AvaloniaProperty.Register<StarRatingControl, IBrush>(
            nameof(FilledBrush),
            defaultValue: Brushes.Gold);

    /// <summary>Brush used for empty stars. Defaults to Gray.</summary>
    public static readonly StyledProperty<IBrush> EmptyBrushProperty =
        AvaloniaProperty.Register<StarRatingControl, IBrush>(
            nameof(EmptyBrush),
            defaultValue: Brushes.Gray);

    // ── CLR wrappers ──────────────────────────────────────────────────────

    public int Rating
    {
        get => GetValue(RatingProperty);
        set => SetValue(RatingProperty, value);
    }

    public double GlyphWidth
    {
        get => GetValue(GlyphWidthProperty);
        set => SetValue(GlyphWidthProperty, value);
    }

    public IBrush FilledBrush
    {
        get => GetValue(FilledBrushProperty);
        set => SetValue(FilledBrushProperty, value);
    }

    public IBrush EmptyBrush
    {
        get => GetValue(EmptyBrushProperty);
        set => SetValue(EmptyBrushProperty, value);
    }

    // ── Constructor ───────────────────────────────────────────────────────

    public StarRatingControl()
    {
        InitializeComponent();
        UpdateStars();
    }

    // ── Property change handling ──────────────────────────────────────────

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == RatingProperty   ||
            change.Property == FilledBrushProperty ||
            change.Property == EmptyBrushProperty)
        {
            UpdateStars();
        }
    }

    // ── Core logic ────────────────────────────────────────────────────────

    private void UpdateStars()
    {
        var slots = new List<StarSlot>(TotalStars);

        for (int i = 1; i <= TotalStars; i++)
        {
            bool filled = i <= Rating;
            slots.Add(new StarSlot(
                Glyph: filled ? FilledGlyph : EmptyGlyph,
                Brush: filled ? FilledBrush  : EmptyBrush));
        }

        StarsHost.ItemsSource = slots;
    }

    private static int CoerceRating(AvaloniaObject _, int value)
        => Math.Clamp(value, 0, TotalStars);

    // ── Inner model ───────────────────────────────────────────────────────

    /// <summary>View-model for a single star slot.</summary>
    public sealed record StarSlot(string Glyph, IBrush Brush);
}
