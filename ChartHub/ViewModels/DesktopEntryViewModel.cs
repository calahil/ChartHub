using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using Avalonia.Threading;

using ChartHub.Localization;
using ChartHub.Services;
using ChartHub.Strings;
using ChartHub.Utilities;

using CommunityToolkit.Mvvm.Input;

namespace ChartHub.ViewModels;

public sealed class DesktopEntryViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AppGlobalSettings? _globalSettings;
    private readonly IAuthSessionService? _authSessionService;
    private readonly IChartHubServerApiClient? _serverApiClient;
    private readonly Func<Action, Task> _uiInvoke;
    private readonly CancellationTokenSource _streamCts = new();
    private bool _streamStarted;

    /// <summary>Raised on the UI thread after a desktop entry is successfully launched.</summary>
    public event EventHandler? AppLaunched;

    public bool IsCompanionMode => OperatingSystem.IsAndroid();

    public bool IsDesktopMode => !OperatingSystem.IsAndroid();

    public DesktopEntryPageStrings PageStrings { get; } = new();

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

    private string _statusMessage = UiLocalization.Get("DesktopEntry.NotInitialized");
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
        IAuthSessionService? authSessionService = null,
        IChartHubServerApiClient? serverApiClient = null,
        Func<Action, Task>? uiInvoke = null)
    {
        _globalSettings = globalSettings;
        _authSessionService = authSessionService;
        _serverApiClient = serverApiClient;
        _uiInvoke = uiInvoke ?? (async action => await Dispatcher.UIThread.InvokeAsync(action));

        _refreshCommand = new AsyncRelayCommand(RefreshAsync);
        _executeCommand = new AsyncRelayCommand<DesktopEntryCardItem?>(ExecuteAsync);
        _killCommand = new AsyncRelayCommand<DesktopEntryCardItem?>(KillAsync);

        // Subscribe to auth state changes (if service available)
        if (_authSessionService is not null)
        {
            _authSessionService.SessionStateChanged += OnAuthSessionStateChanged;
        }

        ObserveBackgroundTask(InitializeAsync(), "DesktopEntry startup sync");
    }

    public DesktopEntryViewModel(
        AppGlobalSettings? globalSettings,
        IChartHubServerApiClient? serverApiClient,
        Func<Action, Task>? uiInvoke = null)
        : this(globalSettings, null, serverApiClient, uiInvoke)
    {
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
                StatusMessage = UiLocalization.Get("DesktopEntry.ConfigureLoad");
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
            await ApplySnapshotAsync(items, baseUrl, UiLocalization.Get("DesktopEntry.ListRefreshed")).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _uiInvoke(() =>
            {
                StatusMessage = UiLocalization.Format("DesktopEntry.RefreshFailed", ex.Message);
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
                StatusMessage = UiLocalization.Get("DesktopEntry.ConfigureExecute");
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
                AppLaunched?.Invoke(this, EventArgs.Empty);
            });
        }
        catch (ChartHubServerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            await _uiInvoke(() =>
            {
                StatusMessage = UiLocalization.Get("DesktopEntry.AlreadyRunning");
            });
        }
        catch (Exception ex)
        {
            await _uiInvoke(() =>
            {
                StatusMessage = UiLocalization.Format("DesktopEntry.ExecuteFailed", ex.Message);
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
                StatusMessage = UiLocalization.Get("DesktopEntry.ConfigureStop");
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
                StatusMessage = UiLocalization.Get("DesktopEntry.StillRunningAfterSigTerm");
            });
        }
        catch (Exception ex)
        {
            await _uiInvoke(() =>
            {
                StatusMessage = UiLocalization.Format("DesktopEntry.StopFailed", ex.Message);
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
                await ApplySnapshotAsync(items, baseUrl, UiLocalization.Get("DesktopEntry.StatusUpdated")).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            await _uiInvoke(() =>
            {
                StatusMessage = UiLocalization.Format("DesktopEntry.StreamDisconnected", ex.Message);
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

            // Reconcile the existing collection in-place so AsyncImageLoader bindings
            // are not torn down and rebuilt on every SSE snapshot (which causes icon flickering).

            // Remove stale entries.
            HashSet<string> incomingIds = new(ordered.Select(e => e.EntryId), StringComparer.Ordinal);
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                if (!incomingIds.Contains(Entries[i].EntryId))
                {
                    Entries.RemoveAt(i);
                }
            }

            // Add new entries and update existing ones, maintaining sorted order.
            for (int i = 0; i < ordered.Length; i++)
            {
                ChartHubServerDesktopEntryResponse entry = ordered[i];
                string? iconUrl = ResolveAbsoluteIconUrl(baseUrl, entry.IconUrl);

                if (existing.TryGetValue(entry.EntryId, out DesktopEntryCardItem? card))
                {
                    card.Name = entry.Name;
                    card.IconUrl = iconUrl;
                    card.Apply(entry.Status, entry.ProcessId);

                    int currentIndex = Entries.IndexOf(card);
                    if (currentIndex != i)
                    {
                        Entries.Move(currentIndex, i);
                    }
                }
                else
                {
                    card = new DesktopEntryCardItem(entry.EntryId, entry.Name, entry.Status, entry.ProcessId, iconUrl);
                    if (i >= Entries.Count)
                    {
                        Entries.Add(card);
                    }
                    else
                    {
                        Entries.Insert(i, card);
                    }
                }
            }

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

    private void OnAuthSessionStateChanged(object? sender, EventArgs e)
    {
        // When auth session state changes, auto-refresh if authenticated
        if (_authSessionService?.CurrentState == AuthSessionState.Authenticated)
        {
            ObserveBackgroundTask(RefreshAsync(), "DesktopEntry auto-refresh after auth");
        }
    }

    public void Dispose()
    {
        // Unsubscribe from auth state changes
        if (_authSessionService is not null)
        {
            _authSessionService.SessionStateChanged -= OnAuthSessionStateChanged;
        }

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

    public string PidLabel => ProcessId.HasValue
        ? UiLocalization.Format("DesktopEntry.PidFormat", ProcessId.Value)
        : UiLocalization.Get("DesktopEntry.PidMissing");

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
