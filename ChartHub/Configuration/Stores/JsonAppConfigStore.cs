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
            PersistRemovedLegacyStorageKeys(rootNode);
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
        if (rootNode["UseMockData"] is not null)
        {
            config.Runtime.UseMockData = rootNode["UseMockData"]?.GetValue<bool>() ?? config.Runtime.UseMockData;
        }

        if (rootNode["ServerApiAuthToken"] is not null)
        {
            config.Runtime.ServerApiAuthToken = rootNode["ServerApiAuthToken"]?.GetValue<string>() ?? config.Runtime.ServerApiAuthToken;
        }
        else if (rootNode["SyncApiAuthToken"] is not null)
        {
            config.Runtime.ServerApiAuthToken = rootNode["SyncApiAuthToken"]?.GetValue<string>() ?? config.Runtime.ServerApiAuthToken;
        }

        if (rootNode["ServerApiBaseUrl"] is not null)
        {
            config.Runtime.ServerApiBaseUrl = rootNode["ServerApiBaseUrl"]?.GetValue<string>() ?? config.Runtime.ServerApiBaseUrl;
        }
        else if (rootNode["SyncApiPreferredBaseUrl"] is not null)
        {
            config.Runtime.ServerApiBaseUrl = rootNode["SyncApiPreferredBaseUrl"]?.GetValue<string>() ?? config.Runtime.ServerApiBaseUrl;
        }

        if (rootNode["InstallLogExpanded"] is not null)
        {
            config.Runtime.InstallLogExpanded = rootNode["InstallLogExpanded"]?.GetValue<bool>() ?? config.Runtime.InstallLogExpanded;
        }

        if (rootNode["AndroidVolumeButtonsControlServerVolume"] is not null)
        {
            config.Runtime.AndroidVolumeButtonsControlServerVolume = rootNode["AndroidVolumeButtonsControlServerVolume"]?.GetValue<bool>()
                ?? config.Runtime.AndroidVolumeButtonsControlServerVolume;
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

    private void PersistRemovedLegacyStorageKeys(JsonObject rootNode)
    {
        if (!StripLegacyStorageKeys(rootNode))
        {
            return;
        }

        _suppressWatcher = true;
        try
        {
            File.WriteAllText(ConfigPath, rootNode.ToJsonString(_serializerOptions));
        }
        finally
        {
            _suppressWatcher = false;
        }
    }

    private static bool StripLegacyStorageKeys(JsonObject rootNode)
    {
        bool changed = false;

        string[] legacyFlatKeys =
        [
            "TempDirectory",
            "DownloadDirectory",
            "StagingDirectory",
            "OutputDirectory",
            "CloneHeroDataDirectory",
            "CloneHeroSongDirectory",
        ];

        foreach (string key in legacyFlatKeys)
        {
            changed |= rootNode.Remove(key);
        }

        if (rootNode["Runtime"] is JsonObject runtimeNode)
        {
            foreach (string key in legacyFlatKeys)
            {
                changed |= runtimeNode.Remove(key);
            }
        }

        return changed;
    }
}
