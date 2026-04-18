using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;

using ChartHub.Models;
using ChartHub.Strings;

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

    // Instrument presence — whether each row should be shown at all.
    public static readonly StyledProperty<bool> HasDrumsProperty =
        AvaloniaProperty.Register<SongRatingControl, bool>(nameof(HasDrums), defaultValue: true);
    public static readonly StyledProperty<bool> HasGuitarProperty =
        AvaloniaProperty.Register<SongRatingControl, bool>(nameof(HasGuitar), defaultValue: true);
    public static readonly StyledProperty<bool> HasBassProperty =
        AvaloniaProperty.Register<SongRatingControl, bool>(nameof(HasBass), defaultValue: true);
    public static readonly StyledProperty<bool> HasVocalsProperty =
        AvaloniaProperty.Register<SongRatingControl, bool>(nameof(HasVocals), defaultValue: true);
    public static readonly StyledProperty<bool> HasKeysProperty =
        AvaloniaProperty.Register<SongRatingControl, bool>(nameof(HasKeys), defaultValue: true);

    // Rated flags — true when tier data is present, false when instrument exists but has no tiers ([]).
    public static readonly StyledProperty<bool> DrumRatedProperty =
        AvaloniaProperty.Register<SongRatingControl, bool>(nameof(DrumRated), defaultValue: true);
    public static readonly StyledProperty<bool> GuitarRatedProperty =
        AvaloniaProperty.Register<SongRatingControl, bool>(nameof(GuitarRated), defaultValue: true);
    public static readonly StyledProperty<bool> BassRatedProperty =
        AvaloniaProperty.Register<SongRatingControl, bool>(nameof(BassRated), defaultValue: true);
    public static readonly StyledProperty<bool> VocalRatedProperty =
        AvaloniaProperty.Register<SongRatingControl, bool>(nameof(VocalRated), defaultValue: true);
    public static readonly StyledProperty<bool> KeysRatedProperty =
        AvaloniaProperty.Register<SongRatingControl, bool>(nameof(KeysRated), defaultValue: true);

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

    public bool HasDrums
    {
        get => GetValue(HasDrumsProperty);
        private set => SetValue(HasDrumsProperty, value);
    }

    public bool HasGuitar
    {
        get => GetValue(HasGuitarProperty);
        private set => SetValue(HasGuitarProperty, value);
    }

    public bool HasBass
    {
        get => GetValue(HasBassProperty);
        private set => SetValue(HasBassProperty, value);
    }

    public bool HasVocals
    {
        get => GetValue(HasVocalsProperty);
        private set => SetValue(HasVocalsProperty, value);
    }

    public bool HasKeys
    {
        get => GetValue(HasKeysProperty);
        private set => SetValue(HasKeysProperty, value);
    }

    public bool DrumRated
    {
        get => GetValue(DrumRatedProperty);
        private set => SetValue(DrumRatedProperty, value);
    }

    public bool GuitarRated
    {
        get => GetValue(GuitarRatedProperty);
        private set => SetValue(GuitarRatedProperty, value);
    }

    public bool BassRated
    {
        get => GetValue(BassRatedProperty);
        private set => SetValue(BassRatedProperty, value);
    }

    public bool VocalRated
    {
        get => GetValue(VocalRatedProperty);
        private set => SetValue(VocalRatedProperty, value);
    }

    public bool KeysRated
    {
        get => GetValue(KeysRatedProperty);
        private set => SetValue(KeysRatedProperty, value);
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
        object? song = Song;
        if (song is EncoreSong e)
        {
            EncoreInstrumentRatings r = ResolveEncoreInstrumentRatings(e);
            HasDrums = r.HasDrums; HasGuitar = r.HasGuitar; HasBass = r.HasBass; HasVocals = r.HasVocals; HasKeys = r.HasKeys;
            DrumRated = r.DrumRated; GuitarRated = r.GuitarRated; BassRated = r.BassRated; VocalRated = r.VocalRated; KeysRated = r.KeysRated;
            DrumString = r.DrumString; GuitarString = r.GuitarString; BassString = r.BassString; VocalString = r.VocalString; KeysString = r.KeysString;
        }
        else if (song is ViewSong sv)
        {
            DrumString = sv.DrumString;
            GuitarString = sv.GuitarString;
            BassString = sv.BassString;
            VocalString = sv.VocalString;
            KeysString = sv.KeysString;
            HasDrums = sv.HasDrums;
            HasGuitar = sv.HasGuitar;
            HasBass = sv.HasBass;
            HasVocals = sv.HasVocals;
            HasKeys = sv.HasKeys;
            DrumRated = sv.DrumRated;
            GuitarRated = sv.GuitarRated;
            BassRated = sv.BassRated;
            VocalRated = sv.VocalRated;
            KeysRated = sv.KeysRated;
        }
        else
        {
            ViewSong rv = RhythmSong;
            DrumString = rv.DrumString;
            GuitarString = rv.GuitarString;
            BassString = rv.BassString;
            VocalString = rv.VocalString;
            KeysString = rv.KeysString;
            HasDrums = rv.HasDrums;
            HasGuitar = rv.HasGuitar;
            HasBass = rv.HasBass;
            HasVocals = rv.HasVocals;
            HasKeys = rv.HasKeys;
            DrumRated = rv.DrumRated;
            GuitarRated = rv.GuitarRated;
            BassRated = rv.BassRated;
            VocalRated = rv.VocalRated;
            KeysRated = rv.KeysRated;
        }
    }

    public SongRatingControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Computes instrument presence and rated flags for an Encore song.
    /// Extracted as a pure static method to allow unit testing without Avalonia.
    /// </summary>
    public static EncoreInstrumentRatings ResolveEncoreInstrumentRatings(EncoreSong e)
    {
        // Guitar family: guitar, guitarghl, guitar_coop, rhythm all share one row.
        bool hasGuitar = e.GuitarDifficulty != null || e.GuitarGhlDifficulty != null
            || e.GuitarCoopDifficulty != null || e.RhythmDifficulty != null;
        int guitarValue = e.GuitarDifficulty ?? e.GuitarGhlDifficulty ?? e.GuitarCoopDifficulty ?? e.RhythmDifficulty ?? 0;

        // Bass family: bass + bassghl share one row.
        bool hasBass = e.BassDifficulty != null || e.BassGhlDifficulty != null;
        int bassValue = e.BassDifficulty ?? e.BassGhlDifficulty ?? 0;

        // Drums family: drums + real drums share one row.
        bool hasDrums = e.DrumsDifficulty != null || e.RealDrumsDifficulty != null;
        int drumsValue = e.DrumsDifficulty ?? e.RealDrumsDifficulty ?? 0;

        bool hasVocals = e.VocalsDifficulty != null;
        int vocalsValue = e.VocalsDifficulty ?? 0;

        bool hasKeys = e.KeysDifficulty != null;
        int keysValue = e.KeysDifficulty ?? 0;

        return new EncoreInstrumentRatings
        {
            HasDrums = hasDrums,
            HasGuitar = hasGuitar,
            HasBass = hasBass,
            HasVocals = hasVocals,
            HasKeys = hasKeys,
            // Rated = charted AND has a real tier (>= 0). Value -1 = charted but unrated → shows "—".
            DrumRated = hasDrums && drumsValue >= 0,
            GuitarRated = hasGuitar && guitarValue >= 0,
            BassRated = hasBass && bassValue >= 0,
            VocalRated = hasVocals && vocalsValue >= 0,
            KeysRated = hasKeys && keysValue >= 0,
            DrumString = Math.Max(0, drumsValue),
            GuitarString = Math.Max(0, guitarValue),
            BassString = Math.Max(0, bassValue),
            VocalString = Math.Max(0, vocalsValue),
            KeysString = Math.Max(0, keysValue),
        };
    }
}

public sealed class EncoreInstrumentRatings
{
    public bool HasDrums { get; init; }
    public bool HasGuitar { get; init; }
    public bool HasBass { get; init; }
    public bool HasVocals { get; init; }
    public bool HasKeys { get; init; }
    public bool DrumRated { get; init; }
    public bool GuitarRated { get; init; }
    public bool BassRated { get; init; }
    public bool VocalRated { get; init; }
    public bool KeysRated { get; init; }
    public int DrumString { get; init; }
    public int GuitarString { get; init; }
    public int BassString { get; init; }
    public int VocalString { get; init; }
    public int KeysString { get; init; }
}