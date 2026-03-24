using System.ComponentModel;
using System.Runtime.CompilerServices;

using ChartHub.Services;

namespace ChartHub.Models;

public sealed class IngestionQueueItem : INotifyPropertyChanged
{
    private bool _checked;
    private ActionResult? _lastActionResult;

    public long IngestionId { get; init; }
    public string Source { get; init; } = string.Empty;
    public string? SourceId { get; init; }
    public string SourceLink { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? Artist { get; init; }
    public string? Title { get; init; }
    public string? Charter { get; init; }
    public IngestionState CurrentState { get; init; }
    public string? DownloadedLocation { get; init; }
    public string? InstalledLocation { get; init; }
    public DesktopState DesktopState { get; init; } = DesktopState.Cloud;
    public string? DesktopLibraryPath { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public string? LibrarySource { get; init; }

    public bool IsInDesktopLibrary => !string.IsNullOrEmpty(InstalledLocation) || DesktopState == DesktopState.Installed;

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

    /// <summary>
    /// The most recent action result (Retry, Install, OpenFolder) for this item. Null if no action has been attempted.
    /// </summary>
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

    /// <summary>
    /// True if an action result exists (successful or failed).
    /// </summary>
    public bool HasActionResult => LastActionResult is not null;

    /// <summary>
    /// True if the most recent action is still pending.
    /// </summary>
    public bool HasPendingActionResult => LastActionResult?.Status == ActionResultStatus.Pending;

    /// <summary>
    /// Badge icon/text for action status.
    /// </summary>
    public string ActionResultStatusBadge => LastActionResult?.StatusBadge ?? string.Empty;

    /// <summary>
    /// Display text for the UI showing action status and message.
    /// </summary>
    public string ActionResultDisplay => LastActionResult?.DisplayText ?? string.Empty;

    public string UpdatedText => UpdatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public bool CanInstall => CurrentState is IngestionState.Downloaded or IngestionState.Staged or IngestionState.Converted;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
