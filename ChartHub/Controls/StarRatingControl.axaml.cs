using Avalonia;
using Avalonia.Controls;
using System.Collections.Generic;

namespace ChartHub.Controls;

/// <summary>
/// Displays a 0-5 rating using uniform fixed-width icon cells.
/// Each slot is either a filled icon (at or below Rating) or an empty icon (above Rating).
/// </summary>
public partial class StarRatingControl : UserControl
{
    private const string EmptyIcon = "avares://ChartHub/Resources/Svg/radio_button_unchecked_24dp.svg";
    private const string FilledIcon = "avares://ChartHub/Resources/Svg/radio_button_checked_24dp.svg";
    private const int TotalStars = 5;

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

    /// <summary>Image URI used for filled rating slots.</summary>
    public static readonly StyledProperty<string> FilledIconSourceProperty =
        AvaloniaProperty.Register<StarRatingControl, string>(
            nameof(FilledIconSource),
            defaultValue: FilledIcon);

    /// <summary>Image URI used for empty rating slots.</summary>
    public static readonly StyledProperty<string> EmptyIconSourceProperty =
        AvaloniaProperty.Register<StarRatingControl, string>(
            nameof(EmptyIconSource),
            defaultValue: EmptyIcon);

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

    public string FilledIconSource
    {
        get => GetValue(FilledIconSourceProperty);
        set => SetValue(FilledIconSourceProperty, value);
    }

    public string EmptyIconSource
    {
        get => GetValue(EmptyIconSourceProperty);
        set => SetValue(EmptyIconSourceProperty, value);
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

        if (change.Property == RatingProperty ||
            change.Property == FilledIconSourceProperty ||
            change.Property == EmptyIconSourceProperty)
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
                IconSource: filled ? FilledIconSource : EmptyIconSource));
        }

        StarsHost.ItemsSource = slots;
    }

    private static int CoerceRating(AvaloniaObject _, int value)
        => Math.Clamp(value, 0, TotalStars);

    // ── Inner model ───────────────────────────────────────────────────────

    public sealed record StarSlot(string IconSource);
}
