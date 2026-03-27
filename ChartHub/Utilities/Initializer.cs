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
    private const int LegacySyncApiMaxRequestBodyBytes = 64 * 1024;
    private const int DefaultSyncApiMaxRequestBodyBytes = 32 * 1024 * 1024;
    private const int MinSyncApiMaxRequestBodyBytes = 4 * 1024;
    private const int MaxSyncApiMaxRequestBodyBytes = 256 * 1024 * 1024;
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
        set => QueueConfigUpdate(config => config.Runtime.DownloadDirectory = NormalizeDownloadDirectory(value));
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
        bool isAndroid = OperatingSystem.IsAndroid();
        string appStorageRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChartHub");
        string cloneHeroDefaultRoot = GetDefaultCloneHeroDataDirectory();

        string tempDir = ResolveWritableDirectory(
            Normalize(Runtime.TempDirectory, appStorageRoot),
            appStorageRoot,
            isAndroid,
            appStorageRoot);

        string downloadDir = ResolveWritableDirectory(
            Normalize(Runtime.DownloadDirectory, Path.Combine(tempDir, "Downloads")),
            Path.Combine(tempDir, "Downloads"),
            isAndroid,
            appStorageRoot);

        string stagingDir = ResolveWritableDirectory(
            Normalize(Runtime.StagingDirectory, Path.Combine(tempDir, "Staging")),
            Path.Combine(tempDir, "Staging"),
            isAndroid,
            appStorageRoot);

        string outputDir = ResolveWritableDirectory(
            Normalize(Runtime.OutputDirectory, Path.Combine(tempDir, "Output")),
            Path.Combine(tempDir, "Output"),
            isAndroid,
            appStorageRoot);

        string cloneHeroDataDir = ResolveWritableDirectory(
            Normalize(Runtime.CloneHeroDataDirectory, cloneHeroDefaultRoot),
            cloneHeroDefaultRoot,
            isAndroid,
            cloneHeroDefaultRoot);

        string cloneHeroSongsDir = ResolveWritableDirectory(
            Normalize(Runtime.CloneHeroSongDirectory, Path.Combine(cloneHeroDataDir, "Songs")),
            Path.Combine(cloneHeroDataDir, "Songs"),
            isAndroid,
            cloneHeroDefaultRoot);
        string syncApiAuthToken = string.IsNullOrWhiteSpace(Runtime.SyncApiAuthToken)
            ? GenerateSyncApiToken()
            : Runtime.SyncApiAuthToken;
        string syncApiDeviceLabel = NormalizeSyncDeviceLabel(Runtime.SyncApiDeviceLabel);
        int syncApiPairCodeTtlMinutes = ClampSyncPairCodeTtlMinutes(Runtime.SyncApiPairCodeTtlMinutes);
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        DateTimeOffset syncApiPairCodeIssuedAt = DateTimeOffset.TryParse(Runtime.SyncApiPairCodeIssuedAtUtc, out DateTimeOffset parsedIssuedAt)
            ? parsedIssuedAt
            : nowUtc;
        bool shouldRotateSyncPairCode = string.IsNullOrWhiteSpace(Runtime.SyncApiPairCode)
            || nowUtc > syncApiPairCodeIssuedAt.AddMinutes(syncApiPairCodeTtlMinutes);
        string syncApiPairCode = shouldRotateSyncPairCode
            ? GenerateSyncPairCode()
            : Runtime.SyncApiPairCode;
        string syncApiPairCodeIssuedAtUtc = shouldRotateSyncPairCode
            ? nowUtc.ToString("O")
            : syncApiPairCodeIssuedAt.ToString("O");
        string syncApiLastPairedDeviceLabel = Runtime.SyncApiLastPairedDeviceLabel ?? string.Empty;
        string syncApiLastPairedAtUtc = Runtime.SyncApiLastPairedAtUtc ?? string.Empty;
        string syncApiPairingHistoryJson = NormalizeSyncConnectionsJson(Runtime.SyncApiPairingHistoryJson);
        string syncApiSavedConnectionsJson = NormalizeSyncConnectionsJson(Runtime.SyncApiSavedConnectionsJson);
        int syncApiMaxRequestBodyBytes = Runtime.SyncApiMaxRequestBodyBytes == LegacySyncApiMaxRequestBodyBytes
            ? DefaultSyncApiMaxRequestBodyBytes
            : ClampSyncApiMaxRequestBodyBytes(Runtime.SyncApiMaxRequestBodyBytes);
        int syncApiBodyReadTimeoutMs = Runtime.SyncApiBodyReadTimeoutMs <= 1000
            ? 30_000
            : ClampSyncApiTimeoutMs(Runtime.SyncApiBodyReadTimeoutMs);
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

    private static string NormalizeDownloadDirectory(string? value)
    {
        string appStorageRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChartHub");
        string fallback = Path.Combine(appStorageRoot, "Downloads");
        string candidate = Normalize(value, fallback);

        if (!OperatingSystem.IsAndroid())
        {
            return candidate;
        }

        return ResolveWritableDirectory(candidate, fallback, enforceScopedRoot: true, scopedRoot: appStorageRoot);
    }

    private static string ResolveWritableDirectory(
        string candidate,
        string fallback,
        bool enforceScopedRoot,
        string scopedRoot)
    {
        if (IsDirectoryWritable(candidate, enforceScopedRoot, scopedRoot))
        {
            return candidate;
        }

        if (IsDirectoryWritable(fallback, enforceScopedRoot, scopedRoot))
        {
            return fallback;
        }

        return fallback;
    }

    private static bool IsDirectoryWritable(string path, bool enforceScopedRoot, string scopedRoot)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string fullPath;
        string fullScopedRoot;
        try
        {
            fullPath = Path.GetFullPath(path);
            fullScopedRoot = Path.GetFullPath(scopedRoot);
        }
        catch
        {
            return false;
        }

        if (enforceScopedRoot && !IsPathUnderRoot(fullPath, fullScopedRoot))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(fullPath);
            string probePath = Path.Combine(fullPath, $".write-probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probePath, "ok");
            File.Delete(probePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPathUnderRoot(string path, string root)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        string normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return path.Equals(normalizedRoot, comparison)
            || path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison)
            || path.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, comparison);
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

    internal void UpdateSyncApiPairingState(
        string pairCode,
        DateTimeOffset pairCodeIssuedAtUtc,
        string deviceLabel,
        DateTimeOffset pairedAtUtc,
        string pairingHistoryJson)
    {
        string normalizedPairCode = pairCode ?? string.Empty;
        string normalizedDeviceLabel = deviceLabel ?? string.Empty;
        string normalizedHistoryJson = NormalizeSyncConnectionsJson(pairingHistoryJson);
        string issuedAtUtc = pairCodeIssuedAtUtc.ToString("O");
        string pairedAtUtcValue = pairedAtUtc.ToString("O");

        Runtime.SyncApiPairCode = normalizedPairCode;
        Runtime.SyncApiPairCodeIssuedAtUtc = issuedAtUtc;
        Runtime.SyncApiLastPairedDeviceLabel = normalizedDeviceLabel;
        Runtime.SyncApiLastPairedAtUtc = pairedAtUtcValue;
        Runtime.SyncApiPairingHistoryJson = normalizedHistoryJson;

        QueueConfigUpdate(config =>
        {
            config.Runtime.SyncApiPairCode = normalizedPairCode;
            config.Runtime.SyncApiPairCodeIssuedAtUtc = issuedAtUtc;
            config.Runtime.SyncApiLastPairedDeviceLabel = normalizedDeviceLabel;
            config.Runtime.SyncApiLastPairedAtUtc = pairedAtUtcValue;
            config.Runtime.SyncApiPairingHistoryJson = normalizedHistoryJson;
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
