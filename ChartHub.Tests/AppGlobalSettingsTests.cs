using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using ChartHub.Tests.TestInfrastructure;
using ChartHub.Utilities;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public class AppGlobalSettingsTests
{
    [Fact]
    public async Task Constructor_WhenRuntimeDirectoriesAreFirstInstall_ResolvesAndCreatesLocalStorageDirectories()
    {
        AppConfigRoot config = new()
        {
            Runtime = new RuntimeAppConfig
            {
                TempDirectory = "first_install",
                DownloadDirectory = "first_install",
                StagingDirectory = "first_install",
                OutputDirectory = "first_install",
                CloneHeroDataDirectory = "first_install",
                CloneHeroSongDirectory = "first_install",
            },
        };

        using var settings = new AppGlobalSettings(new FakeSettingsOrchestrator(config));

        bool initialized = await WaitForConditionAsync(
            () => !string.IsNullOrWhiteSpace(settings.DownloadDir)
                && !string.Equals(settings.DownloadDir, "first_install", StringComparison.Ordinal),
            TimeSpan.FromSeconds(2));

        Assert.True(initialized);
        Assert.StartsWith(settings.TempDir, settings.DownloadDir, StringComparison.Ordinal);
        Assert.StartsWith(settings.TempDir, settings.StagingDir, StringComparison.Ordinal);
        Assert.StartsWith(settings.TempDir, settings.OutputDir, StringComparison.Ordinal);
        Assert.True(Directory.Exists(settings.TempDir));
        Assert.True(Directory.Exists(settings.DownloadDir));
        Assert.True(Directory.Exists(settings.StagingDir));
        Assert.True(Directory.Exists(settings.OutputDir));
    }

    [Fact]
    public async Task Constructor_WhenSyncTokenMissing_GeneratesTokenAndPersists()
    {
        using var temp = new TemporaryDirectoryFixture("app-global-settings-token");
        AppConfigRoot config = CreateConfig(temp.RootPath);
        config.Runtime.ServerApiAuthToken = string.Empty;

        var orchestrator = new BlockingSettingsOrchestrator(config);
        using var settings = new AppGlobalSettings(orchestrator);

        Assert.False(string.IsNullOrWhiteSpace(settings.ServerApiAuthToken));

        orchestrator.ReleasePendingUpdate();

        bool persisted = await WaitForConditionAsync(
            () => orchestrator.UpdateCount > 0
                && !string.IsNullOrWhiteSpace(orchestrator.Current.Runtime.ServerApiAuthToken),
            TimeSpan.FromSeconds(2));
        Assert.True(persisted);
    }

    private static AppConfigRoot CreateConfig(string rootPath)
    {
        string tempDirectory = Path.Combine(rootPath, "Temp");
        string downloadDirectory = Path.Combine(rootPath, "Downloads");
        string stagingDirectory = Path.Combine(rootPath, "Staging");
        string outputDirectory = Path.Combine(rootPath, "Output");
        string cloneHeroDataDirectory = Path.Combine(rootPath, "CloneHero");
        string cloneHeroSongDirectory = Path.Combine(cloneHeroDataDirectory, "Songs");

        Directory.CreateDirectory(tempDirectory);
        Directory.CreateDirectory(downloadDirectory);
        Directory.CreateDirectory(stagingDirectory);
        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(cloneHeroDataDirectory);
        Directory.CreateDirectory(cloneHeroSongDirectory);

        return new AppConfigRoot
        {
            Runtime = new RuntimeAppConfig
            {
                TempDirectory = tempDirectory,
                DownloadDirectory = downloadDirectory,
                StagingDirectory = stagingDirectory,
                OutputDirectory = outputDirectory,
                CloneHeroDataDirectory = cloneHeroDataDirectory,
                CloneHeroSongDirectory = cloneHeroSongDirectory,
            },
        };
    }

    private static async Task<bool> WaitForConditionAsync(Func<bool> predicate, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (predicate())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return predicate();
    }

    private sealed class FakeSettingsOrchestrator(AppConfigRoot current) : ISettingsOrchestrator
    {
        public AppConfigRoot Current { get; private set; } = current;

        public event Action<AppConfigRoot>? SettingsChanged;

        public Task<ConfigValidationResult> UpdateAsync(Action<AppConfigRoot> update, CancellationToken cancellationToken = default)
        {
            update(Current);
            SettingsChanged?.Invoke(Current);
            return Task.FromResult(ConfigValidationResult.Success);
        }

        public Task ReloadAsync(CancellationToken cancellationToken = default)
        {
            SettingsChanged?.Invoke(Current);
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingSettingsOrchestrator(AppConfigRoot current) : ISettingsOrchestrator
    {
        private readonly TaskCompletionSource _releaseUpdate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AppConfigRoot Current { get; private set; } = current;

        public int UpdateCount { get; private set; }

        public event Action<AppConfigRoot>? SettingsChanged;

        public async Task<ConfigValidationResult> UpdateAsync(Action<AppConfigRoot> update, CancellationToken cancellationToken = default)
        {
            await _releaseUpdate.Task.WaitAsync(cancellationToken);
            update(Current);
            UpdateCount++;
            SettingsChanged?.Invoke(Current);
            return ConfigValidationResult.Success;
        }

        public Task ReloadAsync(CancellationToken cancellationToken = default)
        {
            SettingsChanged?.Invoke(Current);
            return Task.CompletedTask;
        }

        public void ReleasePendingUpdate()
        {
            if (!_releaseUpdate.Task.IsCompleted)
            {
                _releaseUpdate.SetResult();
            }
        }
    }
}
