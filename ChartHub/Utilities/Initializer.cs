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
    }

}

public class AppGlobalSettings : INotifyPropertyChanged, IDisposable
{
    private const int MinSyncApiMaxRequestBodyBytes = 4 * 1024;
    private const int MaxSyncApiMaxRequestBodyBytes = 1024 * 1024;
    private const int MinSyncApiTimeoutMs = 100;
    private const int MaxSyncApiTimeoutMs = 30_000;
    private const int MinSyncPairCodeTtlMinutes = 1;
    private const int MaxSyncPairCodeTtlMinutes = 1440;

    private static string GetDefaultCloneHeroDataDirectory()
    {
        if (OperatingSystem.IsAndroid())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CloneHero");
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".clonehero");
    }

    private readonly ISettingsOrchestrator _settingsOrchestrator;
    private readonly SemaphoreSlim _updateLock = new(1, 1);

    private RuntimeAppConfig Runtime => _settingsOrchestrator.Current.Runtime;

    public bool UseMockData
    {
        get => Runtime.UseMockData;
        set => QueueConfigUpdate(config => config.Runtime.UseMockData = value);
    }

    public string TempDir
    {
        get => Runtime.TempDirectory;
        set => QueueConfigUpdate(config => config.Runtime.TempDirectory = value);
    }

    public string StagingDir
    {
        get => Runtime.StagingDirectory;
        set => QueueConfigUpdate(config => config.Runtime.StagingDirectory = value);
    }

    public string DownloadDir
    {
        get => Runtime.DownloadDirectory;
        set => QueueConfigUpdate(config => config.Runtime.DownloadDirectory = value);
    }

    public string OutputDir
    {
        get => Runtime.OutputDirectory;
        set => QueueConfigUpdate(config => config.Runtime.OutputDirectory = value);
    }

    public string CloneHeroDataDir
    {
        get => Runtime.CloneHeroDataDirectory;
        set => QueueConfigUpdate(config => config.Runtime.CloneHeroDataDirectory = value);
    }

    public string CloneHeroSongsDir
    {
        get => Runtime.CloneHeroSongDirectory;
        set => QueueConfigUpdate(config => config.Runtime.CloneHeroSongDirectory = value);
    }

    public string SyncApiAuthToken
    {
        get => Runtime.SyncApiAuthToken;
        set => QueueConfigUpdate(config => config.Runtime.SyncApiAuthToken = value);
    }

    public string SyncApiDesktopBaseUrl
    {
        get => NormalizeSyncApiBaseUrl(Runtime.SyncApiDesktopBaseUrl);
        set => QueueConfigUpdate(config => config.Runtime.SyncApiDesktopBaseUrl = NormalizeSyncApiBaseUrl(value));
    }

    public string SyncApiListenPrefix
    {
        get => NormalizeSyncApiListenPrefix(Runtime.SyncApiListenPrefix);
        set => QueueConfigUpdate(config => config.Runtime.SyncApiListenPrefix = NormalizeSyncApiListenPrefix(value));
    }

    public string SyncApiAdvertisedBaseUrl
    {
        get => NormalizeSyncApiAdvertisedBaseUrl(Runtime.SyncApiAdvertisedBaseUrl);
        set => QueueConfigUpdate(config => config.Runtime.SyncApiAdvertisedBaseUrl = NormalizeSyncApiAdvertisedBaseUrl(value));
    }

    public string SyncApiDeviceLabel
    {
        get => NormalizeSyncDeviceLabel(Runtime.SyncApiDeviceLabel);
        set => QueueConfigUpdate(config => config.Runtime.SyncApiDeviceLabel = NormalizeSyncDeviceLabel(value));
    }

    public string SyncApiPairCode
    {
        get => Runtime.SyncApiPairCode ?? string.Empty;
        set => QueueConfigUpdate(config => config.Runtime.SyncApiPairCode = value ?? string.Empty);
    }

    public string SyncApiPairCodeIssuedAtUtc
    {
        get => Runtime.SyncApiPairCodeIssuedAtUtc ?? string.Empty;
        set => QueueConfigUpdate(config => config.Runtime.SyncApiPairCodeIssuedAtUtc = value ?? string.Empty);
    }

    public int SyncApiPairCodeTtlMinutes
    {
        get => ClampSyncPairCodeTtlMinutes(Runtime.SyncApiPairCodeTtlMinutes);
        set => QueueConfigUpdate(config => config.Runtime.SyncApiPairCodeTtlMinutes = ClampSyncPairCodeTtlMinutes(value));
    }

    public string SyncApiLastPairedDeviceLabel
    {
        get => Runtime.SyncApiLastPairedDeviceLabel ?? string.Empty;
        set => QueueConfigUpdate(config => config.Runtime.SyncApiLastPairedDeviceLabel = value ?? string.Empty);
    }

    public string SyncApiLastPairedAtUtc
    {
        get => Runtime.SyncApiLastPairedAtUtc ?? string.Empty;
        set => QueueConfigUpdate(config => config.Runtime.SyncApiLastPairedAtUtc = value ?? string.Empty);
    }

    public string SyncApiPairingHistoryJson
    {
        get => NormalizeSyncConnectionsJson(Runtime.SyncApiPairingHistoryJson);
        set => QueueConfigUpdate(config => config.Runtime.SyncApiPairingHistoryJson = NormalizeSyncConnectionsJson(value));
    }

    public string SyncApiSavedConnectionsJson
    {
        get => NormalizeSyncConnectionsJson(Runtime.SyncApiSavedConnectionsJson);
        set => QueueConfigUpdate(config => config.Runtime.SyncApiSavedConnectionsJson = NormalizeSyncConnectionsJson(value));
    }

    public bool AllowSyncApiStateOverride
    {
        get => Runtime.AllowSyncApiStateOverride;
        set => QueueConfigUpdate(config => config.Runtime.AllowSyncApiStateOverride = value);
    }

    public int SyncApiMaxRequestBodyBytes
    {
        get => ClampSyncApiMaxRequestBodyBytes(Runtime.SyncApiMaxRequestBodyBytes);
        set => QueueConfigUpdate(config => config.Runtime.SyncApiMaxRequestBodyBytes = ClampSyncApiMaxRequestBodyBytes(value));
    }

    public int SyncApiBodyReadTimeoutMs
    {
        get => ClampSyncApiTimeoutMs(Runtime.SyncApiBodyReadTimeoutMs);
        set => QueueConfigUpdate(config => config.Runtime.SyncApiBodyReadTimeoutMs = ClampSyncApiTimeoutMs(value));
    }

    public int SyncApiMutationWaitTimeoutMs
    {
        get => ClampSyncApiTimeoutMs(Runtime.SyncApiMutationWaitTimeoutMs);
        set => QueueConfigUpdate(config => config.Runtime.SyncApiMutationWaitTimeoutMs = ClampSyncApiTimeoutMs(value));
    }

    public int SyncApiSlowRequestThresholdMs
    {
        get => ClampSyncApiTimeoutMs(Runtime.SyncApiSlowRequestThresholdMs);
        set => QueueConfigUpdate(config => config.Runtime.SyncApiSlowRequestThresholdMs = ClampSyncApiTimeoutMs(value));
    }

    public int TransferOrchestratorConcurrencyCap
    {
        get => Runtime.TransferOrchestratorConcurrencyCap;
        set => QueueConfigUpdate(config => config.Runtime.TransferOrchestratorConcurrencyCap = Math.Clamp(value, 1, 8));
    }

    public bool InstallLogExpanded
    {
        get => Runtime.InstallLogExpanded;
        set => QueueConfigUpdate(config => config.Runtime.InstallLogExpanded = value);
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
        string tempDir = Normalize(Runtime.TempDirectory, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChartHub"));
        string downloadDir = Normalize(Runtime.DownloadDirectory, Path.Combine(tempDir, "Downloads"));
        string stagingDir = Normalize(Runtime.StagingDirectory, Path.Combine(tempDir, "Staging"));
        string outputDir = Normalize(Runtime.OutputDirectory, Path.Combine(tempDir, "Output"));
        string cloneHeroDataDir = Normalize(Runtime.CloneHeroDataDirectory, GetDefaultCloneHeroDataDirectory());
        string cloneHeroSongsDir = Normalize(Runtime.CloneHeroSongDirectory, Path.Combine(cloneHeroDataDir, "Songs"));
        string syncApiAuthToken = string.IsNullOrWhiteSpace(Runtime.SyncApiAuthToken)
            ? GenerateSyncApiToken()
            : Runtime.SyncApiAuthToken;
        string syncApiDesktopBaseUrl = NormalizeSyncApiBaseUrl(Runtime.SyncApiDesktopBaseUrl);
        string syncApiListenPrefix = NormalizeSyncApiListenPrefix(Runtime.SyncApiListenPrefix);
        string syncApiAdvertisedBaseUrl = NormalizeSyncApiAdvertisedBaseUrl(Runtime.SyncApiAdvertisedBaseUrl);
        string syncApiDeviceLabel = NormalizeSyncDeviceLabel(Runtime.SyncApiDeviceLabel);
        string syncApiPairCode = string.IsNullOrWhiteSpace(Runtime.SyncApiPairCode)
            ? GenerateSyncPairCode()
            : Runtime.SyncApiPairCode;
        string syncApiPairCodeIssuedAtUtc = NormalizeSyncPairCodeIssuedAt(Runtime.SyncApiPairCodeIssuedAtUtc);
        int syncApiPairCodeTtlMinutes = ClampSyncPairCodeTtlMinutes(Runtime.SyncApiPairCodeTtlMinutes);
        string syncApiLastPairedDeviceLabel = Runtime.SyncApiLastPairedDeviceLabel ?? string.Empty;
        string syncApiLastPairedAtUtc = Runtime.SyncApiLastPairedAtUtc ?? string.Empty;
        string syncApiPairingHistoryJson = NormalizeSyncConnectionsJson(Runtime.SyncApiPairingHistoryJson);
        string syncApiSavedConnectionsJson = NormalizeSyncConnectionsJson(Runtime.SyncApiSavedConnectionsJson);
        int syncApiMaxRequestBodyBytes = ClampSyncApiMaxRequestBodyBytes(Runtime.SyncApiMaxRequestBodyBytes);
        int syncApiBodyReadTimeoutMs = ClampSyncApiTimeoutMs(Runtime.SyncApiBodyReadTimeoutMs);
        int syncApiMutationWaitTimeoutMs = ClampSyncApiTimeoutMs(Runtime.SyncApiMutationWaitTimeoutMs);
        int syncApiSlowRequestThresholdMs = ClampSyncApiTimeoutMs(Runtime.SyncApiSlowRequestThresholdMs);
        int transferConcurrencyCap = Math.Clamp(Runtime.TransferOrchestratorConcurrencyCap, 1, 8);

        FileTools.CreateDirectoryIfNotExists(tempDir);
        FileTools.CreateDirectoryIfNotExists(downloadDir);
        FileTools.CreateDirectoryIfNotExists(stagingDir);
        FileTools.CreateDirectoryIfNotExists(outputDir);
        FileTools.CreateDirectoryIfNotExists(cloneHeroDataDir);
        FileTools.CreateDirectoryIfNotExists(cloneHeroSongsDir);

        QueueConfigUpdate(config =>
        {
            config.Runtime.TempDirectory = tempDir;
            config.Runtime.DownloadDirectory = downloadDir;
            config.Runtime.StagingDirectory = stagingDir;
            config.Runtime.OutputDirectory = outputDir;
            config.Runtime.CloneHeroDataDirectory = cloneHeroDataDir;
            config.Runtime.CloneHeroSongDirectory = cloneHeroSongsDir;
            config.Runtime.SyncApiAuthToken = syncApiAuthToken;
            config.Runtime.SyncApiDesktopBaseUrl = syncApiDesktopBaseUrl;
            config.Runtime.SyncApiListenPrefix = syncApiListenPrefix;
            config.Runtime.SyncApiAdvertisedBaseUrl = syncApiAdvertisedBaseUrl;
            config.Runtime.SyncApiDeviceLabel = syncApiDeviceLabel;
            config.Runtime.SyncApiPairCode = syncApiPairCode;
            config.Runtime.SyncApiPairCodeIssuedAtUtc = syncApiPairCodeIssuedAtUtc;
            config.Runtime.SyncApiPairCodeTtlMinutes = syncApiPairCodeTtlMinutes;
            config.Runtime.SyncApiLastPairedDeviceLabel = syncApiLastPairedDeviceLabel;
            config.Runtime.SyncApiLastPairedAtUtc = syncApiLastPairedAtUtc;
            config.Runtime.SyncApiPairingHistoryJson = syncApiPairingHistoryJson;
            config.Runtime.SyncApiSavedConnectionsJson = syncApiSavedConnectionsJson;
            config.Runtime.SyncApiMaxRequestBodyBytes = syncApiMaxRequestBodyBytes;
            config.Runtime.SyncApiBodyReadTimeoutMs = syncApiBodyReadTimeoutMs;
            config.Runtime.SyncApiMutationWaitTimeoutMs = syncApiMutationWaitTimeoutMs;
            config.Runtime.SyncApiSlowRequestThresholdMs = syncApiSlowRequestThresholdMs;
            config.Runtime.TransferOrchestratorConcurrencyCap = transferConcurrencyCap;
        });
    }

    private static string GenerateSyncApiToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes);
    }

    private static string Normalize(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) || value == "first_install"
            ? fallback
            : value;
    }

    private static int ClampSyncApiMaxRequestBodyBytes(int value)
    {
        return Math.Clamp(value, MinSyncApiMaxRequestBodyBytes, MaxSyncApiMaxRequestBodyBytes);
    }

    private static string NormalizeSyncApiBaseUrl(string? value)
    {
        string? candidate = value?.Trim();
        return string.IsNullOrWhiteSpace(candidate)
            ? "http://127.0.0.1:15123"
            : candidate;
    }

    private static string NormalizeSyncApiListenPrefix(string? value)
    {
        string? candidate = value?.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return "http://127.0.0.1:15123/";
        }

        if (!candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            candidate = $"http://{candidate}";
        }

        return candidate.EndsWith('/') ? candidate : $"{candidate}/";
    }

    private static string NormalizeSyncApiAdvertisedBaseUrl(string? value)
    {
        string? candidate = value?.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
        }

        if (!candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            candidate = $"http://{candidate}";
        }

        return candidate.EndsWith('/') ? candidate[..^1] : candidate;
    }

    private static string NormalizeSyncDeviceLabel(string? value)
    {
        string? candidate = value?.Trim();
        return string.IsNullOrWhiteSpace(candidate)
            ? "Android Companion"
            : candidate;
    }

    private static string NormalizeSyncConnectionsJson(string? value)
    {
        string? candidate = value?.Trim();
        return string.IsNullOrWhiteSpace(candidate)
            ? "[]"
            : candidate;
    }

    private static string NormalizeSyncPairCodeIssuedAt(string? value)
    {
        if (DateTimeOffset.TryParse(value, out _))
        {
            return value!;
        }

        return DateTimeOffset.UtcNow.ToString("O");
    }

    public static string GenerateSyncPairCode()
    {
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        uint number = BitConverter.ToUInt32(bytes) % 1_000_000;
        return number.ToString("D6");
    }

    private static int ClampSyncPairCodeTtlMinutes(int value)
    {
        return Math.Clamp(value, MinSyncPairCodeTtlMinutes, MaxSyncPairCodeTtlMinutes);
    }

    private static int ClampSyncApiTimeoutMs(int value)
    {
        return Math.Clamp(value, MinSyncApiTimeoutMs, MaxSyncApiTimeoutMs);
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
        OnPropertyChanged(nameof(TempDir));
        OnPropertyChanged(nameof(StagingDir));
        OnPropertyChanged(nameof(DownloadDir));
        OnPropertyChanged(nameof(OutputDir));
        OnPropertyChanged(nameof(CloneHeroDataDir));
        OnPropertyChanged(nameof(CloneHeroSongsDir));
        OnPropertyChanged(nameof(SyncApiAuthToken));
        OnPropertyChanged(nameof(SyncApiDesktopBaseUrl));
        OnPropertyChanged(nameof(SyncApiListenPrefix));
        OnPropertyChanged(nameof(SyncApiAdvertisedBaseUrl));
        OnPropertyChanged(nameof(SyncApiDeviceLabel));
        OnPropertyChanged(nameof(SyncApiPairCode));
        OnPropertyChanged(nameof(SyncApiPairCodeIssuedAtUtc));
        OnPropertyChanged(nameof(SyncApiPairCodeTtlMinutes));
        OnPropertyChanged(nameof(SyncApiLastPairedDeviceLabel));
        OnPropertyChanged(nameof(SyncApiLastPairedAtUtc));
        OnPropertyChanged(nameof(SyncApiPairingHistoryJson));
        OnPropertyChanged(nameof(SyncApiSavedConnectionsJson));
        OnPropertyChanged(nameof(AllowSyncApiStateOverride));
        OnPropertyChanged(nameof(SyncApiMaxRequestBodyBytes));
        OnPropertyChanged(nameof(SyncApiBodyReadTimeoutMs));
        OnPropertyChanged(nameof(SyncApiMutationWaitTimeoutMs));
        OnPropertyChanged(nameof(SyncApiSlowRequestThresholdMs));
        OnPropertyChanged(nameof(TransferOrchestratorConcurrencyCap));
        OnPropertyChanged(nameof(InstallLogExpanded));
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
