using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ChartHub.Models;


public class ViewSong : INotifyPropertyChanged
{
    private string? _artist;
    public string? Artist
    {
        get => _artist;
        set
        {
            _artist = value;
            OnPropertyChanged();
        }
    }

    private string? _title;
    public string? Title
    {
        get => _title;
        set
        {
            _title = value;
            OnPropertyChanged();
        }
    }

    private string? _album;
    public string? Album
    {
        get => _album;
        set
        {
            _album = value;
            OnPropertyChanged();
        }
    }

    private string? _year;
    public string? Year
    {
        get => _year;
        set
        {
            _year = value;
            OnPropertyChanged();
        }
    }

    private string? _genre;
    public string? Genre
    {
        get => _genre;
        set
        {
            _genre = value;
            OnPropertyChanged();
        }
    }

    private long? _downloads;
    public long? Downloads
    {
        get => _downloads;
        set
        {
            _downloads = value;
            OnPropertyChanged();
        }
    }

    private Author? _author;
    public Author? Author
    {
        get => _author;
        set
        {
            _author = value;
            OnPropertyChanged();
        }
    }

    private string? _avatar;
    public string? Avatar
    {
        get => _avatar;
        set
        {
            _avatar = value;
            OnPropertyChanged();
        }
    }

    private string? _albumArt;
    public string? AlbumArt
    {
        get => _albumArt;
        set
        {
            _albumArt = value;
            OnPropertyChanged();
        }
    }

    private long? _songLength;
    public long? SongLength
    {
        get => _songLength;
        set
        {
            _songLength = value;
            OnPropertyChanged();
        }
    }

    private string? _downloadLink;
    public string? DownloadLink
    {
        get => _downloadLink;
        set
        {
            _downloadLink = value;
            OnPropertyChanged();
        }
    }

    private string? _sourceName;
    public string? SourceName
    {
        get => _sourceName;
        set
        {
            _sourceName = value;
            OnPropertyChanged();
        }
    }

    private string? _sourceId;
    public string? SourceId
    {
        get => _sourceId;
        set
        {
            _sourceId = value;
            OnPropertyChanged();
        }
    }

    private bool _isInLibrary;
    public bool IsInLibrary
    {
        get => _isInLibrary;
        set
        {
            _isInLibrary = value;
            OnPropertyChanged();
        }
    }

    private string? _fileName;
    public string? FileName
    {
        get => _fileName;
        set
        {
            _fileName = value;
            OnPropertyChanged();
        }
    }

    private long? _fileSize;
    public long? FileSize
    {
        get => _fileSize;
        set
        {
            _fileSize = value;
            OnPropertyChanged();
        }
    }

    private string? _formattedTime;
    public string? FormattedTime
    {
        get => _formattedTime;
        set
        {
            _formattedTime = value;
            OnPropertyChanged();
        }
    }

    private string? _gameformat;
    public string? Gameformat
    {
        get => _gameformat;
        set
        {
            _gameformat = value;
            OnPropertyChanged();
        }
    }

    private int _drumString;
    public int DrumString
    {
        get => _drumString;
        set
        {
            _drumString = value;
            OnPropertyChanged();
        }
    }

    private int _guitarString;
    public int GuitarString
    {
        get => _guitarString;
        set
        {
            _guitarString = value;
            OnPropertyChanged();
        }
    }

    private int _bassString;
    public int BassString
    {
        get => _bassString;
        set
        {
            _bassString = value;
            OnPropertyChanged();
        }
    }

    private int _vocalString;
    public int VocalString
    {
        get => _vocalString;
        set
        {
            _vocalString = value;
            OnPropertyChanged();
        }
    }

    private int _keysString;
    public int KeysString
    {
        get => _keysString;
        set
        {
            _keysString = value;
            OnPropertyChanged();
        }
    }

    // Instrument presence flags — derived from file.difficulties on RhythmVerse payloads.
    // Default true so non-RhythmVerse paths (Encore, library) continue showing all rows.
    private bool _hasDrums = true;
    public bool HasDrums
    {
        get => _hasDrums;
        set
        {
            _hasDrums = value;
            OnPropertyChanged();
        }
    }

    private bool _hasGuitar = true;
    public bool HasGuitar
    {
        get => _hasGuitar;
        set
        {
            _hasGuitar = value;
            OnPropertyChanged();
        }
    }

    private bool _hasBass = true;
    public bool HasBass
    {
        get => _hasBass;
        set
        {
            _hasBass = value;
            OnPropertyChanged();
        }
    }

    private bool _hasVocals = true;
    public bool HasVocals
    {
        get => _hasVocals;
        set
        {
            _hasVocals = value;
            OnPropertyChanged();
        }
    }

    private bool _hasKeys = true;
    public bool HasKeys
    {
        get => _hasKeys;
        set
        {
            _hasKeys = value;
            OnPropertyChanged();
        }
    }

    // Rated flags — true when the instrument has tier data, false when present but unrated ([]).
    private bool _drumRated = true;
    public bool DrumRated
    {
        get => _drumRated;
        set
        {
            _drumRated = value;
            OnPropertyChanged();
        }
    }

    private bool _guitarRated = true;
    public bool GuitarRated
    {
        get => _guitarRated;
        set
        {
            _guitarRated = value;
            OnPropertyChanged();
        }
    }

    private bool _bassRated = true;
    public bool BassRated
    {
        get => _bassRated;
        set
        {
            _bassRated = value;
            OnPropertyChanged();
        }
    }

    private bool _vocalRated = true;
    public bool VocalRated
    {
        get => _vocalRated;
        set
        {
            _vocalRated = value;
            OnPropertyChanged();
        }
    }

    private bool _keysRated = true;
    public bool KeysRated
    {
        get => _keysRated;
        set
        {
            _keysRated = value;
            OnPropertyChanged();
        }
    }

    private long? _comments;
    public long? Comments
    {
        get => _comments;
        set
        {
            _comments = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
