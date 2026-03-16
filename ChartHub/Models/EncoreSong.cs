using System.ComponentModel;
using System.Runtime.CompilerServices;
using ChartHub.Services;

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
    public long? SongLengthMs { get; set; }
    public int? GuitarDifficulty { get; set; }
    public int? DrumsDifficulty { get; set; }
    public int? BassDifficulty { get; set; }
    public int? VocalsDifficulty { get; set; }
    public int? KeysDifficulty { get; set; }
    public bool HasVideoBackground { get; set; }

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

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}