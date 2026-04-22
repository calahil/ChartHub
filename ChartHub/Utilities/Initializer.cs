using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

using Avalonia.Threading;

using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;

namespace ChartHub.Utilities;

public class Initializer
{
    public Initializer() { }

    public static async Task InitializeAsync()
    {
        string TempDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChartHub");
        string DownloadDir = Path.Combine(TempDir, "Downloads");
        string StagingDir = Path.Combine(TempDir, "Staging");
        string OutputDir = Path.Combine(TempDir, "Output");
        string CloneHeroDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".clonehero");
        string CloneHeroSongsDir = Path.Combine(CloneHeroDataDir, "Songs");

        await Task.CompletedTask;
    }
}

public class AppGlobalSettings : INotifyPropertyChanged, IDisposable
{
    private readonly ISettingsOrchestrator _settingsOrchestrator;
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private readonly object _snapshotLock = new();
    private string _serverApiAuthToken = string.Empty;
    private string _serverApiBaseUrl = string.Empty;

    private RuntimeAppConfig Runtime => _settingsOrchestrator.Current.Runtime;

    public bool UseMockData
    {
        get => Runtime.UseMockData;
        set => QueueConfigUpdate(config => config.Runtime.UseMockData = value);
    }

    public string ServerApiAuthToken
    {
        get
        {
            lock (_snapshotLock)
            {
                return _serverApiAuthToken;
            }
        }
        set
        {
            string normalized = value ?? string.Empty;
            lock (_snapshotLock)
            {
                _serverApiAuthToken = normalized;
            }

            QueueConfigUpdate(config => config.Runtime.ServerApiAuthToken = normalized);
        }
    }

    public string ServerApiBaseUrl
    {
        get
        {
            lock (_snapshotLock)
            {
                return _serverApiBaseUrl;
            }
        }
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            lock (_snapshotLock)
            {
                _serverApiBaseUrl = normalized;
            }

            QueueConfigUpdate(config => config.Runtime.ServerApiBaseUrl = normalized);
        }
    }

    public bool InstallLogExpanded
    {
        get => Runtime.InstallLogExpanded;
        set => QueueConfigUpdate(config => config.Runtime.InstallLogExpanded = value);
    }

    public string UiCulture
    {
        get => Runtime.UiCulture;
        set => QueueConfigUpdate(config => config.Runtime.UiCulture = string.IsNullOrWhiteSpace(value) ? "en-US" : value.Trim());
    }

    public bool AndroidVolumeButtonsControlServerVolume
    {
        get => Runtime.AndroidVolumeButtonsControlServerVolume;
        set => QueueConfigUpdate(config => config.Runtime.AndroidVolumeButtonsControlServerVolume = value);
    }

    public double MouseSpeedMultiplier
    {
        get => Runtime.MouseSpeedMultiplier;
        set => QueueConfigUpdate(config => config.Runtime.MouseSpeedMultiplier = Math.Max(0.1, value));
    }

    public int LastSelectedMainTabIndex
    {
        get => Runtime.LastSelectedMainTabIndex;
        set => QueueConfigUpdate(config => config.Runtime.LastSelectedMainTabIndex = Math.Max(0, value));
    }

    public AppGlobalSettings(ISettingsOrchestrator settingsOrchestrator)
    {
        _settingsOrchestrator = settingsOrchestrator;
        SyncSnapshotFromRuntime();
        _settingsOrchestrator.SettingsChanged += OnSettingsChanged;
        EnsureDefaults();
    }

    private void OnSettingsChanged(AppConfigRoot _)
    {
        SyncSnapshotFromRuntime();
        Dispatcher.UIThread.Post(RaiseAllSettingsChanged);
    }

    private void SyncSnapshotFromRuntime()
    {
        lock (_snapshotLock)
        {
            _serverApiAuthToken = Runtime.ServerApiAuthToken ?? string.Empty;
            _serverApiBaseUrl = Runtime.ServerApiBaseUrl?.Trim() ?? string.Empty;
        }
    }

    private void EnsureDefaults()
    {
        string serverApiBaseUrl = Runtime.ServerApiBaseUrl?.Trim() ?? string.Empty;
        string uiCulture = string.IsNullOrWhiteSpace(Runtime.UiCulture) ? "en-US" : Runtime.UiCulture.Trim();
        Runtime.ServerApiBaseUrl = serverApiBaseUrl;
        Runtime.UiCulture = uiCulture;
        lock (_snapshotLock)
        {
            _serverApiBaseUrl = serverApiBaseUrl;
        }

        QueueConfigUpdate(config =>
        {
            config.Runtime.ServerApiBaseUrl = serverApiBaseUrl;
            config.Runtime.UiCulture = uiCulture;
        });
    }


    private void QueueConfigUpdate(Action<AppConfigRoot> update)
    {
        _ = Task.Run(async () =>
        {
            await _updateLock.WaitAsync().ConfigureAwait(false);
            try
            {
                ConfigValidationResult result = await _settingsOrchestrator.UpdateAsync(update).ConfigureAwait(false);
                if (!result.IsValid)
                {
                    string errors = string.Join("; ", result.Failures.Select(f => $"{f.Key}: {f.Message}"));
                    Logger.LogWarning("Config", "Invalid settings update ignored", new Dictionary<string, object?>
                    {
                        ["fieldKeys"] = string.Join(",", result.Failures.Select(f => f.Key)),
                        ["details"] = errors,
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Config", "Failed to update settings asynchronously", ex);
            }
            finally
            {
                _updateLock.Release();
            }
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void RaiseAllSettingsChanged()
    {
        OnPropertyChanged(nameof(UseMockData));
        OnPropertyChanged(nameof(ServerApiAuthToken));
        OnPropertyChanged(nameof(ServerApiBaseUrl));
        OnPropertyChanged(nameof(InstallLogExpanded));
        OnPropertyChanged(nameof(UiCulture));
        OnPropertyChanged(nameof(AndroidVolumeButtonsControlServerVolume));
        OnPropertyChanged(nameof(MouseSpeedMultiplier));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        _settingsOrchestrator.SettingsChanged -= OnSettingsChanged;
        _updateLock.Dispose();
    }
}
