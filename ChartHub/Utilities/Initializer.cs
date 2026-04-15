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

    private RuntimeAppConfig Runtime => _settingsOrchestrator.Current.Runtime;

    public bool UseMockData
    {
        get => Runtime.UseMockData;
        set => QueueConfigUpdate(config => config.Runtime.UseMockData = value);
    }

    public string ServerApiAuthToken
    {
        get => Runtime.ServerApiAuthToken;
        set => QueueConfigUpdate(config => config.Runtime.ServerApiAuthToken = value);
    }

    public string ServerApiBaseUrl
    {
        get => Runtime.ServerApiBaseUrl ?? string.Empty;
        set => QueueConfigUpdate(config => config.Runtime.ServerApiBaseUrl = value?.Trim() ?? string.Empty);
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

    public AppGlobalSettings(ISettingsOrchestrator settingsOrchestrator)
    {
        _settingsOrchestrator = settingsOrchestrator;
        _settingsOrchestrator.SettingsChanged += OnSettingsChanged;
        EnsureDefaults();
    }

    private void OnSettingsChanged(AppConfigRoot _)
    {
        Dispatcher.UIThread.Post(RaiseAllSettingsChanged);
    }

    private void EnsureDefaults()
    {
        string serverApiAuthToken = string.IsNullOrWhiteSpace(Runtime.ServerApiAuthToken)
            ? GenerateSyncApiToken()
            : Runtime.ServerApiAuthToken;
        string serverApiBaseUrl = Runtime.ServerApiBaseUrl?.Trim() ?? string.Empty;
        string uiCulture = string.IsNullOrWhiteSpace(Runtime.UiCulture) ? "en-US" : Runtime.UiCulture.Trim();
        Runtime.ServerApiAuthToken = serverApiAuthToken;
        Runtime.ServerApiBaseUrl = serverApiBaseUrl;
        Runtime.UiCulture = uiCulture;

        QueueConfigUpdate(config =>
        {
            config.Runtime.ServerApiAuthToken = serverApiAuthToken;
            config.Runtime.ServerApiBaseUrl = serverApiBaseUrl;
            config.Runtime.UiCulture = uiCulture;
        });
    }

    private static string GenerateSyncApiToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes);
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
