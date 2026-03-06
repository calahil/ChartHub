using RhythmVerseClient.Api;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RhythmVerseClient.Models
{

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

        private string? _formattedTme;
        public string? FormattedTme
        {
            get => _formattedTme;
            set
            {
                _formattedTme = value;
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

        private string? _drumString;
        public string? DrumString
        {
            get => _drumString;
            set
            {
                _drumString = value;
                OnPropertyChanged();
            }
        }

        private string? _guitarString;
        public string? GuitarString
        {
            get => _guitarString;
            set
            {
                _guitarString = value;
                OnPropertyChanged();
            }
        }

        private string? _bassString;
        public string? BassString
        {
            get => _bassString;
            set
            {
                _bassString = value;
                OnPropertyChanged();
            }
        }

        private string? _vocalString;
        public string? VocalString
        {
            get => _vocalString;
            set
            {
                _vocalString = value;
                OnPropertyChanged();
            }
        }

        private string? _keysString;
        public string? KeysString
        {
            get => _keysString;
            set
            {
                _keysString = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
