using System.Text.Json.Serialization;

namespace ChartHub.Models;

public sealed class EncoreSortOption
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("direction")]
    public string Direction { get; set; } = "asc";
}

public sealed class EncoreTextFilter
{
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("exact")]
    public bool Exact { get; set; }

    [JsonPropertyName("exclude")]
    public bool Exclude { get; set; }
}

public class EncoreGeneralSearchRequest
{
    [JsonPropertyName("search")]
    public string Search { get; set; } = "*";

    [JsonPropertyName("per_page")]
    public int PerPage { get; set; } = 25;

    [JsonPropertyName("page")]
    public int Page { get; set; } = 1;

    [JsonPropertyName("instrument")]
    public string? Instrument { get; set; }

    [JsonPropertyName("difficulty")]
    public string? Difficulty { get; set; }

    [JsonPropertyName("drumType")]
    public string? DrumType { get; set; }

    [JsonPropertyName("drumsReviewed")]
    public bool DrumsReviewed { get; set; } = true;

    [JsonPropertyName("sort")]
    public EncoreSortOption? Sort { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "bridge";
}

public sealed class EncoreAdvancedSearchRequest : EncoreGeneralSearchRequest
{
    [JsonPropertyName("name")]
    public EncoreTextFilter? Name { get; set; }

    [JsonPropertyName("artist")]
    public EncoreTextFilter? Artist { get; set; }

    [JsonPropertyName("album")]
    public EncoreTextFilter? Album { get; set; }

    [JsonPropertyName("genre")]
    public EncoreTextFilter? Genre { get; set; }

    [JsonPropertyName("year")]
    public EncoreTextFilter? Year { get; set; }

    [JsonPropertyName("charter")]
    public EncoreTextFilter? Charter { get; set; }

    [JsonPropertyName("minLength")]
    public int? MinLength { get; set; }

    [JsonPropertyName("maxLength")]
    public int? MaxLength { get; set; }

    [JsonPropertyName("minIntensity")]
    public int? MinIntensity { get; set; }

    [JsonPropertyName("maxIntensity")]
    public int? MaxIntensity { get; set; }

    [JsonPropertyName("minAverageNPS")]
    public double? MinAverageNps { get; set; }

    [JsonPropertyName("maxAverageNPS")]
    public double? MaxAverageNps { get; set; }

    [JsonPropertyName("minMaxNPS")]
    public double? MinMaxNps { get; set; }

    [JsonPropertyName("maxMaxNPS")]
    public double? MaxMaxNps { get; set; }

    [JsonPropertyName("minYear")]
    public int? MinYear { get; set; }

    [JsonPropertyName("maxYear")]
    public int? MaxYear { get; set; }

    [JsonPropertyName("modifiedAfter")]
    public string? ModifiedAfter { get; set; }

    [JsonPropertyName("hash")]
    public string? Hash { get; set; }

    [JsonPropertyName("chartHash")]
    public string? ChartHash { get; set; }

    [JsonPropertyName("trackHash")]
    public string? TrackHash { get; set; }

    [JsonPropertyName("hasSoloSections")]
    public bool? HasSoloSections { get; set; }

    [JsonPropertyName("hasForcedNotes")]
    public bool? HasForcedNotes { get; set; }

    [JsonPropertyName("hasOpenNotes")]
    public bool? HasOpenNotes { get; set; }

    [JsonPropertyName("hasTapNotes")]
    public bool? HasTapNotes { get; set; }

    [JsonPropertyName("hasLyrics")]
    public bool? HasLyrics { get; set; }

    [JsonPropertyName("hasVocals")]
    public bool? HasVocals { get; set; }

    [JsonPropertyName("hasRollLanes")]
    public bool? HasRollLanes { get; set; }

    [JsonPropertyName("has2xKick")]
    public bool? Has2xKick { get; set; }

    [JsonPropertyName("hasIssues")]
    public bool? HasIssues { get; set; }

    [JsonPropertyName("hasVideoBackground")]
    public bool? HasVideoBackground { get; set; }

    [JsonPropertyName("modchart")]
    public bool? Modchart { get; set; }
}

public sealed class EncoreSearchResponse
{
    [JsonPropertyName("found")]
    public int Found { get; set; }

    [JsonPropertyName("out_of")]
    public int OutOf { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("search_time_ms")]
    public int SearchTimeMs { get; set; }

    [JsonPropertyName("data")]
    public List<EncoreSongDto> Data { get; set; } = [];
}

public sealed class EncoreAdvancedSearchResponse
{
    [JsonPropertyName("found")]
    public int Found { get; set; }

    [JsonPropertyName("data")]
    public List<EncoreSongDto> Data { get; set; } = [];
}

public sealed class EncoreSongDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("artist")]
    public string? Artist { get; set; }

    [JsonPropertyName("album")]
    public string? Album { get; set; }

    [JsonPropertyName("genre")]
    public string? Genre { get; set; }

    [JsonPropertyName("year")]
    public string? Year { get; set; }

    [JsonPropertyName("charter")]
    public string? Charter { get; set; }

    [JsonPropertyName("chartId")]
    public int ChartId { get; set; }

    [JsonPropertyName("songId")]
    public int? SongId { get; set; }

    [JsonPropertyName("groupId")]
    public int GroupId { get; set; }

    [JsonPropertyName("albumArtMd5")]
    public string? AlbumArtMd5 { get; set; }

    [JsonPropertyName("md5")]
    public string Md5 { get; set; } = string.Empty;

    [JsonPropertyName("song_length")]
    public long? SongLength { get; set; }

    [JsonPropertyName("diff_guitar")]
    public int? DiffGuitar { get; set; }

    [JsonPropertyName("diff_bass")]
    public int? DiffBass { get; set; }

    [JsonPropertyName("diff_drums")]
    public int? DiffDrums { get; set; }

    [JsonPropertyName("diff_vocals")]
    public int? DiffVocals { get; set; }

    [JsonPropertyName("diff_keys")]
    public int? DiffKeys { get; set; }

    [JsonPropertyName("hasVideoBackground")]
    public bool HasVideoBackground { get; set; }
}