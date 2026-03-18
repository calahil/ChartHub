using System.ComponentModel;
using System.Runtime.CompilerServices;
using ChartHub.Services;
using ChartHub.Utilities;

namespace ChartHub.Models;

public sealed class EncoreSong : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _artist = string.Empty;
    private string _album = string.Empty;
    private string _genre = string.Empty;
    private string _year = string.Empty;
    private string _charter = string.Empty;
    private string _albumArtUrl = string.Empty;
    private string _downloadUrl = string.Empty;
    private string _formattedTime = string.Empty;
    private string _sourceName = LibrarySourceNames.Encore;
    private string _sourceId = string.Empty;
    private bool _isInLibrary;

    public int ChartId { get; set; }
    public int? SongId { get; set; }
    public int GroupId { get; set; }
    public string Md5 { get; set; } = string.Empty;
    public string ChartHash { get; set; } = string.Empty;
    public int VersionGroupId { get; set; }
    public string ApplicationUsername { get; set; } = string.Empty;
    public long? SongLengthMs { get; set; }
    public long? PreviewStartTimeMs { get; set; }
    public int? BandDifficulty { get; set; }
    public int? GuitarDifficulty { get; set; }
    public int? GuitarCoopDifficulty { get; set; }
    public int? RhythmDifficulty { get; set; }
    public int? DrumsDifficulty { get; set; }
    public int? RealDrumsDifficulty { get; set; }
    public int? BassDifficulty { get; set; }
    public int? GuitarGhlDifficulty { get; set; }
    public int? BassGhlDifficulty { get; set; }
    public int? VocalsDifficulty { get; set; }
    public int? KeysDifficulty { get; set; }
    public bool HasVideoBackground { get; set; }
    public bool? Modchart { get; set; }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string Artist
    {
        get => _artist;
        set => SetField(ref _artist, value);
    }

    public string Album
    {
        get => _album;
        set => SetField(ref _album, value);
    }

    public string Genre
    {
        get => _genre;
        set => SetField(ref _genre, value);
    }

    public string Year
    {
        get => _year;
        set => SetField(ref _year, value);
    }

    public string Charter
    {
        get => _charter;
        set => SetField(ref _charter, value);
    }

    public string AlbumArtUrl
    {
        get => _albumArtUrl;
        set => SetField(ref _albumArtUrl, value);
    }

    public string DownloadUrl
    {
        get => _downloadUrl;
        set => SetField(ref _downloadUrl, value);
    }

    public string FormattedTime
    {
        get => _formattedTime;
        set => SetField(ref _formattedTime, value);
    }

    public string SourceName
    {
        get => _sourceName;
        set => SetField(ref _sourceName, value);
    }

    public string SourceId
    {
        get => _sourceId;
        set => SetField(ref _sourceId, value);
    }

    public bool IsInLibrary
    {
        get => _isInLibrary;
        set => SetField(ref _isInLibrary, value);
    }

    public IEnumerable<string> GetCatalogSourceIds()
    {
        if (ChartId > 0)
            yield return ChartId.ToString();

        if (!string.IsNullOrWhiteSpace(Md5))
            yield return Md5;
    }

    public ViewSong ToViewSong(string? fileName = null)
    {
        var normalizedSongLengthSeconds = SongLengthMs.HasValue
            ? Math.Max(0, SongLengthMs.Value / 1000)
            : 0;
        var normalizedAuthorName = string.IsNullOrWhiteSpace(ApplicationUsername)
            ? Charter
            : ApplicationUsername;
        var sourceId = !string.IsNullOrWhiteSpace(SourceId)
            ? SourceId
            : (ChartId > 0 ? ChartId.ToString() : Md5);

        return new ViewSong
        {
            Artist = Artist,
            Title = Name,
            Album = Album,
            Year = Year,
            Genre = Genre,
            Downloads = 0,
            Comments = 0,
            Author = new Author
            {
                Name = normalizedAuthorName,
                Shortname = ApplicationUsername,
                AvatarPath = "avares://ChartHub/Resources/Images/blankprofile.png",
            },
            Avatar = "avares://ChartHub/Resources/Images/blankprofile.png",
            AlbumArt = string.IsNullOrWhiteSpace(AlbumArtUrl)
                ? "avares://ChartHub/Resources/Images/noalbumart.png"
                : AlbumArtUrl,
            SongLength = normalizedSongLengthSeconds,
            DownloadLink = DownloadUrl,
            SourceName = SourceName,
            SourceId = sourceId,
            IsInLibrary = IsInLibrary,
            FileName = fileName,
            FileSize = 0,
            FormattedTime = Toolbox.ConvertMillisecondsToText(SongLengthMs),
            Gameformat = string.Empty,
            DrumString = DrumsDifficulty ?? 0,
            GuitarString = GuitarDifficulty ?? 0,
            BassString = BassDifficulty ?? 0,
            VocalString = VocalsDifficulty ?? 0,
            KeysString = KeysDifficulty ?? 0,
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}