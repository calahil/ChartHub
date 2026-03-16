namespace ChartHub.Configuration.Models;

public sealed class EncoreUiStateConfig
{
    public string SearchText { get; set; } = string.Empty;
    public bool IsAdvancedVisible { get; set; }
    public string? SelectedInstrument { get; set; }
    public string? SelectedDifficulty { get; set; }
    public string? SelectedDrumType { get; set; }
    public bool DrumsReviewed { get; set; } = true;
    public string SelectedSort { get; set; } = "name";
    public string SelectedSortDirection { get; set; } = "asc";
    public string AdvancedName { get; set; } = string.Empty;
    public string AdvancedArtist { get; set; } = string.Empty;
    public string AdvancedAlbum { get; set; } = string.Empty;
    public string AdvancedGenre { get; set; } = string.Empty;
    public string AdvancedYear { get; set; } = string.Empty;
    public string AdvancedCharter { get; set; } = string.Empty;
    public string MinYear { get; set; } = string.Empty;
    public string MaxYear { get; set; } = string.Empty;
    public string MinLength { get; set; } = string.Empty;
    public string MaxLength { get; set; } = string.Empty;
    public bool? HasVideoBackground { get; set; }
    public bool? HasLyrics { get; set; }
    public bool? HasVocals { get; set; }
    public bool? Has2xKick { get; set; }
    public bool? HasIssues { get; set; }
    public bool? Modchart { get; set; }
}