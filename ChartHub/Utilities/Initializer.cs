using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using Avalonia.Threading;

namespace ChartHub.Utilities
{
    public class Initializer
    {
        public Initializer() { }

        public static async Task InitializeAsync()
        {
            var TempDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChartHub");
            var DownloadDir = Path.Combine(TempDir, "Downloads");
            var StagingDir = Path.Combine(TempDir, "Staging");
            var OutputDir = Path.Combine(TempDir, "Output");
            var CloneHeroDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".clonehero");
            var CloneHeroSongsDir = Path.Combine(CloneHeroDataDir, "Songs");
        }

    }

    public class AppGlobalSettings : INotifyPropertyChanged, IDisposable
    {
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

        public bool AllowSyncApiStateOverride
        {
            get => Runtime.AllowSyncApiStateOverride;
            set => QueueConfigUpdate(config => config.Runtime.AllowSyncApiStateOverride = value);
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
            var tempDir = Normalize(Runtime.TempDirectory, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChartHub"));
            var downloadDir = Normalize(Runtime.DownloadDirectory, Path.Combine(tempDir, "Downloads"));
            var stagingDir = Normalize(Runtime.StagingDirectory, Path.Combine(tempDir, "Staging"));
            var outputDir = Normalize(Runtime.OutputDirectory, Path.Combine(tempDir, "Output"));
            var cloneHeroDataDir = Normalize(Runtime.CloneHeroDataDirectory, GetDefaultCloneHeroDataDirectory());
            var cloneHeroSongsDir = Normalize(Runtime.CloneHeroSongDirectory, Path.Combine(cloneHeroDataDir, "Songs"));
            var syncApiAuthToken = string.IsNullOrWhiteSpace(Runtime.SyncApiAuthToken)
                ? GenerateSyncApiToken()
                : Runtime.SyncApiAuthToken;
            var transferConcurrencyCap = Math.Clamp(Runtime.TransferOrchestratorConcurrencyCap, 1, 8);

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

        private void QueueConfigUpdate(Action<AppConfigRoot> update)
        {
            _ = Task.Run(async () =>
            {
                await _updateLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    var result = await _settingsOrchestrator.UpdateAsync(update).ConfigureAwait(false);
                    if (!result.IsValid)
                    {
                        var errors = string.Join("; ", result.Failures.Select(f => $"{f.Key}: {f.Message}"));
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
            OnPropertyChanged(nameof(AllowSyncApiStateOverride));
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
}
