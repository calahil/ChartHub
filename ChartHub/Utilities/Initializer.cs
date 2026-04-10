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

        string serverApiAuthToken = string.IsNullOrWhiteSpace(Runtime.ServerApiAuthToken)
            ? GenerateSyncApiToken()
            : Runtime.ServerApiAuthToken;
        string serverApiBaseUrl = Runtime.ServerApiBaseUrl?.Trim() ?? string.Empty;
        int transferConcurrencyCap = Math.Clamp(Runtime.TransferOrchestratorConcurrencyCap, 1, 8);

        FileTools.CreateDirectoryIfNotExists(tempDir);
        FileTools.CreateDirectoryIfNotExists(downloadDir);
        FileTools.CreateDirectoryIfNotExists(stagingDir);
        FileTools.CreateDirectoryIfNotExists(outputDir);
        FileTools.CreateDirectoryIfNotExists(cloneHeroDataDir);
        FileTools.CreateDirectoryIfNotExists(cloneHeroSongsDir);

        Runtime.TempDirectory = tempDir;
        Runtime.DownloadDirectory = downloadDir;
        Runtime.StagingDirectory = stagingDir;
        Runtime.OutputDirectory = outputDir;
        Runtime.CloneHeroDataDirectory = cloneHeroDataDir;
        Runtime.CloneHeroSongDirectory = cloneHeroSongsDir;
        Runtime.ServerApiAuthToken = serverApiAuthToken;
        Runtime.ServerApiBaseUrl = serverApiBaseUrl;
        Runtime.TransferOrchestratorConcurrencyCap = transferConcurrencyCap;

        QueueConfigUpdate(config =>
        {
            config.Runtime.TempDirectory = tempDir;
            config.Runtime.DownloadDirectory = downloadDir;
            config.Runtime.StagingDirectory = stagingDir;
            config.Runtime.OutputDirectory = outputDir;
            config.Runtime.CloneHeroDataDirectory = cloneHeroDataDir;
            config.Runtime.CloneHeroSongDirectory = cloneHeroSongsDir;
            config.Runtime.ServerApiAuthToken = serverApiAuthToken;
            config.Runtime.ServerApiBaseUrl = serverApiBaseUrl;
            config.Runtime.TransferOrchestratorConcurrencyCap = transferConcurrencyCap;
        });
    }

    private static string GetDefaultCloneHeroDataDirectory()
    {
        if (OperatingSystem.IsAndroid())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CloneHero");
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".clonehero");
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
        OnPropertyChanged(nameof(ServerApiAuthToken));
        OnPropertyChanged(nameof(ServerApiBaseUrl));
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
