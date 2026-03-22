using System.Text.Json;
using System.Text.Json.Nodes;

using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;

namespace ChartHub.Configuration.Stores;

public sealed class JsonAppConfigStore : IAppConfigStore
{
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = true,
    };

    private readonly FileSystemWatcher _watcher;
    private readonly object _changeDebounceSync = new();
    private bool _suppressWatcher;
    private CancellationTokenSource? _changeDebounceCts;

    public string ConfigPath { get; }

    public event Action<AppConfigRoot>? ConfigChanged;

    public JsonAppConfigStore(string configPath)
    {
        ConfigPath = configPath;

        string directory = Path.GetDirectoryName(configPath) ?? throw new ArgumentException("Config path must include a directory.", nameof(configPath));
        string fileName = Path.GetFileName(configPath);

        Directory.CreateDirectory(directory);

        _watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };

        _watcher.Changed += OnConfigFileChanged;
        _watcher.Created += OnConfigFileChanged;
        _watcher.Renamed += OnConfigFileChanged;
    }

    public AppConfigRoot Load()
    {
        if (!File.Exists(ConfigPath))
        {
            var defaultConfig = new AppConfigRoot();
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(defaultConfig, _serializerOptions));
            return defaultConfig;
        }

        string json = File.ReadAllText(ConfigPath);
        AppConfigRoot config = JsonSerializer.Deserialize<AppConfigRoot>(json, _serializerOptions) ?? new AppConfigRoot();

        // Backward compatibility: map legacy flat settings into Runtime if needed.
        if (config.Runtime is null)
        {
            config.Runtime = new RuntimeAppConfig();
        }

        if (config.EncoreUi is null)
        {
            config.EncoreUi = new EncoreUiStateConfig();
        }

        var rootNode = JsonNode.Parse(json) as JsonObject;
        if (rootNode is not null)
        {
            ApplyLegacyFlatMapping(rootNode, config);
        }

        if (config.ConfigVersion <= 0)
        {
            config.ConfigVersion = AppConfigRoot.CurrentVersion;
        }

        return config;
    }

    public async Task SaveAsync(AppConfigRoot config, CancellationToken cancellationToken = default)
    {
        string tempPath = $"{ConfigPath}.tmp";
        string json = JsonSerializer.Serialize(config, _serializerOptions);

        _suppressWatcher = true;
        try
        {
            await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, ConfigPath, overwrite: true);
        }
        finally
        {
            _suppressWatcher = false;
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_suppressWatcher)
        {
            return;
        }

        CancellationTokenSource localCts;
        lock (_changeDebounceSync)
        {
            _changeDebounceCts?.Cancel();
            _changeDebounceCts?.Dispose();
            _changeDebounceCts = new CancellationTokenSource();
            localCts = _changeDebounceCts;
        }

        _ = NotifyConfigChangedDebouncedAsync(localCts.Token);
    }

    private async Task NotifyConfigChangedDebouncedAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Debounce bursty change notifications from temporary-file swap writes.
            await Task.Delay(75, cancellationToken).ConfigureAwait(false);
            AppConfigRoot config = Load();
            ConfigChanged?.Invoke(config);
        }
        catch (OperationCanceledException)
        {
            // Newer file change superseded this pending notification.
        }
        catch
        {
            // Swallow transient read errors from partial writes.
        }
    }

    public void Dispose()
    {
        lock (_changeDebounceSync)
        {
            _changeDebounceCts?.Cancel();
            _changeDebounceCts?.Dispose();
            _changeDebounceCts = null;
        }

        _watcher.Changed -= OnConfigFileChanged;
        _watcher.Created -= OnConfigFileChanged;
        _watcher.Renamed -= OnConfigFileChanged;
        _watcher.Dispose();
    }

    private static void ApplyLegacyFlatMapping(JsonObject rootNode, AppConfigRoot config)
    {
        static string ReadString(JsonObject root, string key)
        {
            string? value = root[key]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(value) ? "first_install" : value;
        }

        if (rootNode["UseMockData"] is not null)
        {
            config.Runtime.UseMockData = rootNode["UseMockData"]?.GetValue<bool>() ?? config.Runtime.UseMockData;
        }

        if (rootNode["TempDirectory"] is not null)
        {
            config.Runtime.TempDirectory = ReadString(rootNode, "TempDirectory");
        }

        if (rootNode["DownloadDirectory"] is not null)
        {
            config.Runtime.DownloadDirectory = ReadString(rootNode, "DownloadDirectory");
        }

        if (rootNode["StagingDirectory"] is not null)
        {
            config.Runtime.StagingDirectory = ReadString(rootNode, "StagingDirectory");
        }

        if (rootNode["OutputDirectory"] is not null)
        {
            config.Runtime.OutputDirectory = ReadString(rootNode, "OutputDirectory");
        }

        if (rootNode["CloneHeroSongDirectory"] is not null)
        {
            config.Runtime.CloneHeroSongDirectory = ReadString(rootNode, "CloneHeroSongDirectory");
        }

        if (rootNode["CloneHeroDataDirectory"] is not null)
        {
            config.Runtime.CloneHeroDataDirectory = ReadString(rootNode, "CloneHeroDataDirectory");
        }

        if (rootNode["SyncApiAuthToken"] is not null)
        {
            config.Runtime.SyncApiAuthToken = rootNode["SyncApiAuthToken"]?.GetValue<string>() ?? config.Runtime.SyncApiAuthToken;
        }

        if (rootNode["SyncApiDesktopBaseUrl"] is not null)
        {
            config.Runtime.SyncApiDesktopBaseUrl = rootNode["SyncApiDesktopBaseUrl"]?.GetValue<string>() ?? config.Runtime.SyncApiDesktopBaseUrl;
        }

        if (rootNode["SyncApiListenPrefix"] is not null)
        {
            config.Runtime.SyncApiListenPrefix = rootNode["SyncApiListenPrefix"]?.GetValue<string>() ?? config.Runtime.SyncApiListenPrefix;
        }

        if (rootNode["SyncApiAdvertisedBaseUrl"] is not null)
        {
            config.Runtime.SyncApiAdvertisedBaseUrl = rootNode["SyncApiAdvertisedBaseUrl"]?.GetValue<string>() ?? config.Runtime.SyncApiAdvertisedBaseUrl;
        }

        if (rootNode["SyncApiDeviceLabel"] is not null)
        {
            config.Runtime.SyncApiDeviceLabel = rootNode["SyncApiDeviceLabel"]?.GetValue<string>() ?? config.Runtime.SyncApiDeviceLabel;
        }

        if (rootNode["SyncApiPairCode"] is not null)
        {
            config.Runtime.SyncApiPairCode = rootNode["SyncApiPairCode"]?.GetValue<string>() ?? config.Runtime.SyncApiPairCode;
        }

        if (rootNode["SyncApiPairCodeIssuedAtUtc"] is not null)
        {
            config.Runtime.SyncApiPairCodeIssuedAtUtc = rootNode["SyncApiPairCodeIssuedAtUtc"]?.GetValue<string>() ?? config.Runtime.SyncApiPairCodeIssuedAtUtc;
        }

        if (rootNode["SyncApiPairCodeTtlMinutes"] is not null)
        {
            config.Runtime.SyncApiPairCodeTtlMinutes = rootNode["SyncApiPairCodeTtlMinutes"]?.GetValue<int>() ?? config.Runtime.SyncApiPairCodeTtlMinutes;
        }

        if (rootNode["SyncApiLastPairedDeviceLabel"] is not null)
        {
            config.Runtime.SyncApiLastPairedDeviceLabel = rootNode["SyncApiLastPairedDeviceLabel"]?.GetValue<string>() ?? config.Runtime.SyncApiLastPairedDeviceLabel;
        }

        if (rootNode["SyncApiLastPairedAtUtc"] is not null)
        {
            config.Runtime.SyncApiLastPairedAtUtc = rootNode["SyncApiLastPairedAtUtc"]?.GetValue<string>() ?? config.Runtime.SyncApiLastPairedAtUtc;
        }

        if (rootNode["SyncApiPairingHistoryJson"] is not null)
        {
            config.Runtime.SyncApiPairingHistoryJson = rootNode["SyncApiPairingHistoryJson"]?.GetValue<string>() ?? config.Runtime.SyncApiPairingHistoryJson;
        }

        if (rootNode["SyncApiSavedConnectionsJson"] is not null)
        {
            config.Runtime.SyncApiSavedConnectionsJson = rootNode["SyncApiSavedConnectionsJson"]?.GetValue<string>() ?? config.Runtime.SyncApiSavedConnectionsJson;
        }

        if (rootNode["AllowSyncApiStateOverride"] is not null)
        {
            config.Runtime.AllowSyncApiStateOverride = rootNode["AllowSyncApiStateOverride"]?.GetValue<bool>() ?? config.Runtime.AllowSyncApiStateOverride;
        }

        if (rootNode["SyncApiMaxRequestBodyBytes"] is not null)
        {
            config.Runtime.SyncApiMaxRequestBodyBytes = rootNode["SyncApiMaxRequestBodyBytes"]?.GetValue<int>() ?? config.Runtime.SyncApiMaxRequestBodyBytes;
        }

        if (rootNode["SyncApiBodyReadTimeoutMs"] is not null)
        {
            config.Runtime.SyncApiBodyReadTimeoutMs = rootNode["SyncApiBodyReadTimeoutMs"]?.GetValue<int>() ?? config.Runtime.SyncApiBodyReadTimeoutMs;
        }

        if (rootNode["SyncApiMutationWaitTimeoutMs"] is not null)
        {
            config.Runtime.SyncApiMutationWaitTimeoutMs = rootNode["SyncApiMutationWaitTimeoutMs"]?.GetValue<int>() ?? config.Runtime.SyncApiMutationWaitTimeoutMs;
        }

        if (rootNode["SyncApiSlowRequestThresholdMs"] is not null)
        {
            config.Runtime.SyncApiSlowRequestThresholdMs = rootNode["SyncApiSlowRequestThresholdMs"]?.GetValue<int>() ?? config.Runtime.SyncApiSlowRequestThresholdMs;
        }

        if (rootNode["InstallLogExpanded"] is not null)
        {
            config.Runtime.InstallLogExpanded = rootNode["InstallLogExpanded"]?.GetValue<bool>() ?? config.Runtime.InstallLogExpanded;
        }

        if (rootNode["GoogleDrive"] is JsonObject googleDrive)
        {
            if (googleDrive["android_client_id"] is not null)
            {
                config.GoogleAuth.AndroidClientId = googleDrive["android_client_id"]?.GetValue<string>();
            }

            if (googleDrive["desktop_client_id"] is not null)
            {
                config.GoogleAuth.DesktopClientId = googleDrive["desktop_client_id"]?.GetValue<string>();
            }
        }
    }
}
