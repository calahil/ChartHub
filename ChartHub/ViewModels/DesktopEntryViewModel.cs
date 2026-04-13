using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using Avalonia.Threading;

using ChartHub.Services;
using ChartHub.Utilities;

using CommunityToolkit.Mvvm.Input;

namespace ChartHub.ViewModels;

public sealed class DesktopEntryViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AppGlobalSettings? _globalSettings;
    private readonly IChartHubServerApiClient? _serverApiClient;
    private readonly Func<Action, Task> _uiInvoke;
    private readonly CancellationTokenSource _streamCts = new();
    private bool _streamStarted;

    public bool IsCompanionMode => OperatingSystem.IsAndroid();

    public bool IsDesktopMode => !OperatingSystem.IsAndroid();

    private ObservableCollection<DesktopEntryCardItem> _entries = [];
    public ObservableCollection<DesktopEntryCardItem> Entries
    {
        get => _entries;
        private set
        {
            _entries = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasEntries));
        }
    }

    public bool HasEntries => Entries.Count > 0;

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value)
            {
                return;
            }

            _isBusy = value;
            OnPropertyChanged();
        }
    }

    private string _statusMessage = "Desktop entry control not initialized.";
    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value)
            {
                return;
            }

            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    private readonly AsyncRelayCommand _refreshCommand;
    public IAsyncRelayCommand RefreshCommand => _refreshCommand;

    private readonly AsyncRelayCommand<DesktopEntryCardItem?> _executeCommand;
    public IAsyncRelayCommand<DesktopEntryCardItem?> ExecuteCommand => _executeCommand;

    private readonly AsyncRelayCommand<DesktopEntryCardItem?> _killCommand;
    public IAsyncRelayCommand<DesktopEntryCardItem?> KillCommand => _killCommand;

    public DesktopEntryViewModel(
        AppGlobalSettings? globalSettings = null,
        IChartHubServerApiClient? serverApiClient = null,
        Func<Action, Task>? uiInvoke = null)
    {
        _globalSettings = globalSettings;
        _serverApiClient = serverApiClient;
        _uiInvoke = uiInvoke ?? (async action => await Dispatcher.UIThread.InvokeAsync(action));

        _refreshCommand = new AsyncRelayCommand(RefreshAsync);
        _executeCommand = new AsyncRelayCommand<DesktopEntryCardItem?>(ExecuteAsync);
        _killCommand = new AsyncRelayCommand<DesktopEntryCardItem?>(KillAsync);

        ObserveBackgroundTask(InitializeAsync(), "DesktopEntry startup sync");
    }

    private async Task InitializeAsync()
    {
        await RefreshAsync().ConfigureAwait(false);

        if (_streamStarted)
        {
            return;
        }

        _streamStarted = true;
        ObserveBackgroundTask(StreamUpdatesAsync(_streamCts.Token), "DesktopEntry stream");
    }

    private async Task RefreshAsync()
    {
        if (!TryGetServerConnection(out string baseUrl, out string bearerToken))
        {
            await _uiInvoke(() =>
            {
                Entries = [];
                StatusMessage = "Configure ChartHub.Server URL and token to load desktop entries.";
            });
            return;
        }

        IsBusy = true;
        try
        {
            await _serverApiClient!.RefreshDesktopEntryCatalogAsync(baseUrl, bearerToken).ConfigureAwait(false);
            IReadOnlyList<ChartHubServerDesktopEntryResponse> items = await _serverApiClient
                .ListDesktopEntriesAsync(baseUrl, bearerToken)
                .ConfigureAwait(false);
            await ApplySnapshotAsync(items, baseUrl, "Desktop entry list refreshed.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _uiInvoke(() =>
            {
                StatusMessage = $"Failed to refresh desktop entries: {ex.Message}";
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteAsync(DesktopEntryCardItem? entry)
    {
        if (entry is null)
        {
            return;
        }

        if (!TryGetServerConnection(out string baseUrl, out string bearerToken))
        {
            await _uiInvoke(() =>
            {
                StatusMessage = "Configure ChartHub.Server URL and token to execute desktop entries.";
            });
            return;
        }

        IsBusy = true;
        try
        {
            ChartHubServerDesktopEntryActionResponse result = await _serverApiClient!
                .ExecuteDesktopEntryAsync(baseUrl, bearerToken, entry.EntryId)
                .ConfigureAwait(false);

            await _uiInvoke(() =>
            {
                DesktopEntryCardItem? item = Entries.FirstOrDefault(candidate => candidate.EntryId == result.EntryId);
                item?.Apply(result.Status, result.ProcessId);
                StatusMessage = result.Message;
            });
        }
        catch (ChartHubServerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            await _uiInvoke(() =>
            {
                StatusMessage = "Desktop entry is already running.";
            });
        }
        catch (Exception ex)
        {
            await _uiInvoke(() =>
            {
                StatusMessage = $"Failed to execute desktop entry: {ex.Message}";
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task KillAsync(DesktopEntryCardItem? entry)
    {
        if (entry is null)
        {
            return;
        }

        if (!TryGetServerConnection(out string baseUrl, out string bearerToken))
        {
            await _uiInvoke(() =>
            {
                StatusMessage = "Configure ChartHub.Server URL and token to stop desktop entries.";
            });
            return;
        }

        IsBusy = true;
        try
        {
            ChartHubServerDesktopEntryActionResponse result = await _serverApiClient!
                .KillDesktopEntryAsync(baseUrl, bearerToken, entry.EntryId)
                .ConfigureAwait(false);

            await _uiInvoke(() =>
            {
                DesktopEntryCardItem? item = Entries.FirstOrDefault(candidate => candidate.EntryId == result.EntryId);
                item?.Apply(result.Status, result.ProcessId);
                StatusMessage = result.Message;
            });
        }
        catch (ChartHubServerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            await _uiInvoke(() =>
            {
                StatusMessage = "SIGTERM sent but process is still running.";
            });
        }
        catch (Exception ex)
        {
            await _uiInvoke(() =>
            {
                StatusMessage = $"Failed to stop desktop entry: {ex.Message}";
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StreamUpdatesAsync(CancellationToken cancellationToken)
    {
        if (!TryGetServerConnection(out string baseUrl, out string bearerToken))
        {
            return;
        }

        try
        {
            await foreach (IReadOnlyList<ChartHubServerDesktopEntryResponse> items in _serverApiClient!
                               .StreamDesktopEntriesAsync(baseUrl, bearerToken, cancellationToken)
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                await ApplySnapshotAsync(items, baseUrl, "Desktop entry status updated.").ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            await _uiInvoke(() =>
            {
                StatusMessage = $"Desktop entry stream disconnected: {ex.Message}";
            });
        }
    }

    private async Task ApplySnapshotAsync(
        IReadOnlyList<ChartHubServerDesktopEntryResponse> entries,
        string baseUrl,
        string statusMessage)
    {
        await _uiInvoke(() =>
        {
            var existing = Entries
                .ToDictionary(item => item.EntryId, item => item, StringComparer.Ordinal);

            ChartHubServerDesktopEntryResponse[] ordered = entries
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            ObservableCollection<DesktopEntryCardItem> updated = new();
            foreach (ChartHubServerDesktopEntryResponse entry in ordered)
            {
                string? iconUrl = ResolveAbsoluteIconUrl(baseUrl, entry.IconUrl);
                if (!existing.TryGetValue(entry.EntryId, out DesktopEntryCardItem? card))
                {
                    card = new DesktopEntryCardItem(entry.EntryId, entry.Name, entry.Status, entry.ProcessId, iconUrl);
                }
                else
                {
                    card.Name = entry.Name;
                    card.IconUrl = iconUrl;
                    card.Apply(entry.Status, entry.ProcessId);
                }

                updated.Add(card);
            }

            Entries = updated;
            StatusMessage = statusMessage;
        });
    }

    private static string? ResolveAbsoluteIconUrl(string baseUrl, string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            return null;
        }

        if (Uri.TryCreate(iconPath, UriKind.Absolute, out Uri? absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute.ToString();
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? baseUri))
        {
            return null;
        }

        return new Uri(baseUri, iconPath).ToString();
    }

    private bool TryGetServerConnection(out string baseUrl, out string bearerToken)
    {
        baseUrl = _globalSettings?.ServerApiBaseUrl?.Trim() ?? string.Empty;
        bearerToken = _globalSettings?.ServerApiAuthToken?.Trim() ?? string.Empty;

        return !string.IsNullOrWhiteSpace(baseUrl)
               && !string.IsNullOrWhiteSpace(bearerToken)
               && _serverApiClient is not null;
    }

    private static void ObserveBackgroundTask(Task task, string context)
    {
        _ = task.ContinueWith(t =>
        {
            Exception? ex = t.Exception?.GetBaseException();
            if (ex is not null)
            {
                Logger.LogError("DesktopEntry", $"{context} failed", ex);
            }
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    public void Dispose()
    {
        _streamCts.Cancel();
        _streamCts.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class DesktopEntryCardItem : INotifyPropertyChanged
{
    public DesktopEntryCardItem(string entryId, string name, string status, int? processId, string? iconUrl)
    {
        EntryId = entryId;
        _name = name;
        _status = status;
        _processId = processId;
        _iconUrl = iconUrl;
    }

    public string EntryId { get; }

    private string _name;
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value)
            {
                return;
            }

            _name = value;
            OnPropertyChanged();
        }
    }

    private string _status;
    public string Status
    {
        get => _status;
        private set
        {
            if (_status == value)
            {
                return;
            }

            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(CanExecute));
            OnPropertyChanged(nameof(CanKill));
        }
    }

    private int? _processId;
    public int? ProcessId
    {
        get => _processId;
        private set
        {
            if (_processId == value)
            {
                return;
            }

            _processId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PidLabel));
        }
    }

    private string? _iconUrl;
    public string? IconUrl
    {
        get => _iconUrl;
        set
        {
            if (_iconUrl == value)
            {
                return;
            }

            _iconUrl = value;
            OnPropertyChanged();
        }
    }

    public bool IsRunning => string.Equals(Status, "Running", StringComparison.OrdinalIgnoreCase);

    public bool CanExecute => !IsRunning;

    public bool CanKill => IsRunning;

    public string PidLabel => ProcessId.HasValue ? $"PID: {ProcessId.Value}" : "PID: -";

    public void Apply(string status, int? processId)
    {
        Status = status;
        ProcessId = processId;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
