using System.ComponentModel;

namespace ChartHub.Services;

public class DownloadFile(string displayName, string filePath, string urlString, long? fileSize) : INotifyPropertyChanged
{
    private string _sourceName = string.Empty;
    public string SourceName
    {
        get => _sourceName;
        set
        {
            if (_sourceName == value)
            {
                return;
            }

            _sourceName = value;
            OnPropertyChanged(nameof(SourceName));
        }
    }

    private string _displayName = displayName;
    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (_displayName == value)
            {
                return;
            }

            _displayName = value;
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    private string _filePath = filePath;
    public string FilePath
    {
        get => _filePath;
        set
        {
            if (_filePath == value)
            {
                return;
            }

            _filePath = value;
            OnPropertyChanged(nameof(FilePath));
        }
    }

    private string _url = urlString;
    public string Url
    {
        get => _url;
        set
        {
            if (_url == value)
            {
                return;
            }

            _url = value;
            OnPropertyChanged(nameof(Url));
        }
    }

    private double _downloadProgress;
    public double DownloadProgress
    {
        get => _downloadProgress;
        set
        {
            _downloadProgress = value;
            OnPropertyChanged(nameof(DownloadProgress));
        }
    }

    private bool _finished;
    public bool Finished
    {
        get => _finished;
        set
        {
            _finished = value;
            OnPropertyChanged(nameof(Finished));
            OnPropertyChanged(nameof(CanCancel));
            OnPropertyChanged(nameof(CanClear));
        }
    }

    private string _status = "Queued";
    public string Status
    {
        get => _status;
        set
        {
            if (_status == value)
            {
                return;
            }

            _status = value;
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(CanCancel));
            OnPropertyChanged(nameof(CanClear));
        }
    }

    public bool CanCancel => !Finished
        && !string.Equals(Status, "Cancelling", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(Status, "Cancelled", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(Status, "Failed", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(Status, "Installed", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(Status, "Downloaded", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(Status, "Completed", StringComparison.OrdinalIgnoreCase);

    public bool CanClear => Finished
        || string.Equals(Status, "Downloaded", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Status, "Installed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Status, "Cancelled", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Status, "Failed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Status, "Completed", StringComparison.OrdinalIgnoreCase);

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (_errorMessage == value)
            {
                return;
            }

            _errorMessage = value;
            OnPropertyChanged(nameof(ErrorMessage));
        }
    }

    private long? _fileSize = fileSize;
    public long? FileSize
    {
        get => _fileSize;
        set
        {
            if (_fileSize == value)
            {
                return;
            }

            _fileSize = value;
            OnPropertyChanged(nameof(FileSize));
        }
    }

    public Action? CancelAction { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public override string ToString()
    {
        return DisplayName;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(DisplayName, FilePath);
    }
}
