using System.ComponentModel;
using System.Runtime.CompilerServices;
using ChartHub.Services;

namespace ChartHub.Models;

public sealed class IngestionQueueItem : INotifyPropertyChanged
{
    private bool _checked;

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

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
                return;

            _checked = value;
            OnPropertyChanged();
        }
    }

    public string UpdatedText => UpdatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public bool CanInstall => CurrentState is IngestionState.Downloaded or IngestionState.Staged or IngestionState.Converted;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
