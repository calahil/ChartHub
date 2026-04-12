using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using ChartHub.Tests.TestInfrastructure;
using ChartHub.Utilities;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public class AppGlobalSettingsTests
{
    [Fact]
    public async Task Constructor_WhenServerTokenMissing_GeneratesAndPersistsToken()
    {
        AppConfigRoot config = new()
        {
            Runtime = new RuntimeAppConfig
            {
                ServerApiAuthToken = string.Empty,
            },
        };

        var orchestrator = new BlockingSettingsOrchestrator(config);
        using var settings = new AppGlobalSettings(orchestrator);

        Assert.False(string.IsNullOrWhiteSpace(settings.ServerApiAuthToken));

        orchestrator.ReleasePendingUpdate();

        bool initialized = await WaitForConditionAsync(
            () => orchestrator.UpdateCount > 0
                && !string.IsNullOrWhiteSpace(orchestrator.Current.Runtime.ServerApiAuthToken),
            TimeSpan.FromSeconds(2));

        Assert.True(initialized);
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
        _ = rootPath;

        return new AppConfigRoot
        {
            Runtime = new RuntimeAppConfig
            {
                ServerApiBaseUrl = "https://localhost:5001",
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
