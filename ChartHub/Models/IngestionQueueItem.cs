using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using ChartHub.Services;

namespace ChartHub.Models;

public sealed class IngestionQueueItem : INotifyPropertyChanged
{
    private bool _checked;
    private ActionResult? _lastActionResult;
    private IngestionState _currentState;
    private string? _downloadedLocation;
    private string? _installedLocation;
    private DesktopState _desktopState = DesktopState.Cloud;
    private DateTimeOffset _updatedAtUtc;
    private string? _charter;
    private string? _artist;
    private string? _title;
    private double _progressPercent;
    private bool _isJobLogExpanded;
    private bool _isLoadingLogs;
    private string? _fileType;

    public long IngestionId { get; init; }
    public string Source { get; init; } = string.Empty;
    public string? SourceId { get; init; }
    public string SourceLink { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? LibrarySource { get; init; }
    public string? DesktopLibraryPath { get; init; }

    public ObservableCollection<ChartHubServerJobLogEntry> JobLogs { get; } = [];

    public string? FileType
    {
        get => _fileType;
        set
        {
            if (_fileType == value)
            {
                return;
            }

            _fileType = value;
            OnPropertyChanged();
        }
    }

    public bool IsInDesktopLibrary => !string.IsNullOrEmpty(InstalledLocation) || DesktopState == DesktopState.Installed;

    public string? Artist
    {
        get => _artist;
        set
        {
            if (_artist == value)
            {
                return;
            }

            _artist = value;
            OnPropertyChanged();
        }
    }

    public string? Title
    {
        get => _title;
        set
        {
            if (_title == value)
            {
                return;
            }

            _title = value;
            OnPropertyChanged();
        }
    }

    public string? Charter
    {
        get => _charter;
        set
        {
            if (_charter == value)
            {
                return;
            }

            _charter = value;
            OnPropertyChanged();
        }
    }

    public IngestionState CurrentState
    {
        get => _currentState;
        set
        {
            if (_currentState == value)
            {
                return;
            }

            _currentState = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanInstall));
            OnPropertyChanged(nameof(IsProgressVisible));
        }
    }

    public string? DownloadedLocation
    {
        get => _downloadedLocation;
        set
        {
            if (_downloadedLocation == value)
            {
                return;
            }

            _downloadedLocation = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DownloadedFilename));
        }
    }

    public string? DownloadedFilename => string.IsNullOrWhiteSpace(_downloadedLocation)
        ? null
        : Path.GetFileName(_downloadedLocation);

    public string? InstalledLocation
    {
        get => _installedLocation;
        set
        {
            if (_installedLocation == value)
            {
                return;
            }

            _installedLocation = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsInDesktopLibrary));
        }
    }

    public DesktopState DesktopState
    {
        get => _desktopState;
        set
        {
            if (_desktopState == value)
            {
                return;
            }

            _desktopState = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsInDesktopLibrary));
        }
    }

    public DateTimeOffset UpdatedAtUtc
    {
        get => _updatedAtUtc;
        set
        {
            if (_updatedAtUtc == value)
            {
                return;
            }

            _updatedAtUtc = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UpdatedText));
        }
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        set
        {
            if (Math.Abs(_progressPercent - value) < 0.001)
            {
                return;
            }

            _progressPercent = value;
            OnPropertyChanged();
        }
    }

    public bool IsJobLogExpanded
    {
        get => _isJobLogExpanded;
        set
        {
            if (_isJobLogExpanded == value)
            {
                return;
            }

            _isJobLogExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(JobLogToggleText));
        }
    }

    public bool IsLoadingLogs
    {
        get => _isLoadingLogs;
        set
        {
            if (_isLoadingLogs == value)
            {
                return;
            }

            _isLoadingLogs = value;
            OnPropertyChanged();
        }
    }

    public string JobLogToggleText => IsJobLogExpanded ? "Collapse Log" : "Expand Log";

    public bool IsProgressVisible => CurrentState is
        IngestionState.Downloading or
        IngestionState.Installing or
        IngestionState.Staged or
        IngestionState.Converting;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
            {
                return;
            }

            _checked = value;
            OnPropertyChanged();
        }
    }

    public ActionResult? LastActionResult
    {
        get => _lastActionResult;
        set
        {
            if (Equals(_lastActionResult, value))
            {
                return;
            }

            _lastActionResult = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasActionResult));
            OnPropertyChanged(nameof(HasPendingActionResult));
            OnPropertyChanged(nameof(ActionResultStatusBadge));
            OnPropertyChanged(nameof(ActionResultDisplay));
        }
    }

    public bool HasActionResult => LastActionResult is not null;
    public bool HasPendingActionResult => LastActionResult?.Status == ActionResultStatus.Pending;
    public string ActionResultStatusBadge => LastActionResult?.StatusBadge ?? string.Empty;
    public string ActionResultDisplay => LastActionResult?.DisplayText ?? string.Empty;

    public string UpdatedText => UpdatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public bool CanInstall => CurrentState is IngestionState.Downloaded or IngestionState.Converted;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
