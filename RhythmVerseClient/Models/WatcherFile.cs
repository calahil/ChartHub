
using System.ComponentModel;
using RhythmVerseClient.Utilities;

namespace RhythmVerseClient.Models
{
    public enum WatcherType
    {
        Directory,
        File
    }

    public enum WatcherFileType
    {
        Con,
        CloneHero,
        Directory,
        Rar,
        SevenZip,
        Unknown,
        Zip
    }

    public class WatcherFile(string displayName, string filePath, WatcherFileType watcherFileType, string imageFile, long sizeBytes) : INotifyPropertyChanged
    {
        private string _imageFile = imageFile;
        private bool _checked = false;
        private string _displayName = displayName;
        private string _filePath = filePath;
        private WatcherFileType _fileType = watcherFileType;
        private string _fileSize = FileTools.ConvertFileSize(sizeBytes);
        private long _sizeBytes = sizeBytes;
        private double _downloadProgress = 0;

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked != value)
                {
                    _checked = value;
                    OnPropertyChanged(nameof(Checked));
                }
            }
        }

        public string ImageFile
        {
            get => _imageFile;
            set
            {
                if (_imageFile != value)
                {
                    _imageFile = value;
                    OnPropertyChanged(nameof(ImageFile));
                }
            }
        }

        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (_displayName != value)
                {
                    _displayName = value;
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public WatcherFileType FileType
        {
            get => _fileType;
            set
            {
                if (_fileType != value)
                {
                    _fileType = value;
                    OnPropertyChanged(nameof(FileType));
                }
            }
        }

        public string FileSize
        {
            get => _fileSize;
            set
            {
                if (_fileSize != value)
                {
                    _fileSize = value;
                    OnPropertyChanged(nameof(FileSize));
                }
            }
        }

        public string FilePath
        {
            get => _filePath;
            set
            {
                if (_filePath != value)
                {
                    _filePath = value;
                    OnPropertyChanged(nameof(FilePath));
                }
            }
        }

        public long SizeBytes
        {
            get => _sizeBytes;
            set
            {
                _sizeBytes = value;
                OnPropertyChanged(nameof(SizeBytes));
            }
        }

        public double DownloadProgress
        {
            get => _downloadProgress;
            set
            {
                _downloadProgress = value;
                OnPropertyChanged(nameof(DownloadProgress));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString()
        {
            return DisplayName;
        }

        public override bool Equals(object? obj)
        {
            if (obj is WatcherFile other)
            {
                return this.DisplayName == other.DisplayName && this.FilePath == other.FilePath;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(DisplayName, FilePath);
        }
    }
}