using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using ChartHub.Services;
using ChartHub.Tests.TestInfrastructure;
using ChartHub.Utilities;
using ChartHub.ViewModels;

namespace ChartHub.Tests;

[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class VolumeViewModelTests
{
    [Fact]
    public void VolumeSessionCardItem_ApplySnapshot_PreservesPendingValueWhenDirty()
    {
        VolumeSessionCardItem item = new(new ChartHubServerVolumeSessionResponse(
            "42",
            "RetroArch",
            1234,
            "RetroArch",
            40,
            false));
        item.PendingVolume = 75;

        item.ApplySnapshot(new ChartHubServerVolumeSessionResponse(
            "42",
            "RetroArch",
            1234,
            "RetroArch",
            45,
            false));

        Assert.Equal(45, item.CurrentVolume);
        Assert.Equal(75, item.PendingVolume);
        Assert.True(item.IsDirty);
    }

    [Fact]
    public async Task AdjustMasterVolumeAsync_UsesPendingValueAndUpdatesState()
    {
        using AppGlobalSettings settings = CreateSettings("http://localhost:5000", "token");
        FakeChartHubServerApiClient apiClient = new()
        {
            State = new ChartHubServerVolumeStateResponse(
                DateTimeOffset.UtcNow,
                new ChartHubServerVolumeMasterResponse(40, false),
                [],
                false,
                "unsupported"),
        };

        using VolumeViewModel sut = new(settings, apiClient, new NoOpVolumeHardwareButtonSource(), action =>
        {
            action();
            return Task.CompletedTask;
        });

        bool initialized = SpinWait.SpinUntil(() => sut.CurrentMasterVolume == 40, TimeSpan.FromSeconds(2));
        Assert.True(initialized);

        sut.PendingMasterVolume = 43;
        await sut.AdjustMasterVolumeAsync(5);

        Assert.Equal(48, apiClient.LastRequestedMasterVolume);
        Assert.Equal(48, sut.CurrentMasterVolume);
        Assert.Equal(48, sut.PendingMasterVolume);
    }

    [Fact]
    public async Task IsAndroidHardwareBindingEnabledUpdatesAppSettingsAsync()
    {
        using AppGlobalSettings settings = CreateSettings("http://localhost:5000", "token");
        using VolumeViewModel sut = new(settings, new FakeChartHubServerApiClient(), new NoOpVolumeHardwareButtonSource(), action =>
        {
            action();
            return Task.CompletedTask;
        });

        sut.IsAndroidHardwareBindingEnabled = true;

        bool updated = await WaitForConditionAsync(
            () => settings.AndroidVolumeButtonsControlServerVolume,
            TimeSpan.FromSeconds(2));

        Assert.True(updated);
        Assert.True(settings.AndroidVolumeButtonsControlServerVolume);
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

    private static AppGlobalSettings CreateSettings(string baseUrl, string token)
    {
        AppConfigRoot config = new()
        {
            Runtime = new RuntimeAppConfig
            {
                ServerApiBaseUrl = baseUrl,
                ServerApiAuthToken = token,
            },
        };

        return new AppGlobalSettings(new FakeSettingsOrchestrator(config));
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

    private sealed class FakeChartHubServerApiClient : IChartHubServerApiClient
    {
        public ChartHubServerVolumeStateResponse State { get; set; } = new(
            DateTimeOffset.UtcNow,
            new ChartHubServerVolumeMasterResponse(0, false),
            [],
            false,
            "unsupported");

        public int LastRequestedMasterVolume { get; private set; } = -1;

        public Task<ChartHubServerAuthExchangeResponse> ExchangeGoogleTokenAsync(string baseUrl, string googleIdToken, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ChartHubServerDownloadJobResponse> CreateDownloadJobAsync(string baseUrl, string bearerToken, ChartHubServerCreateDownloadJobRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ChartHubServerDownloadJobResponse>> ListDownloadJobsAsync(string baseUrl, string bearerToken, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ChartHubServerDownloadJobResponse>>([]);

        public IAsyncEnumerable<IReadOnlyList<ChartHubServerDownloadJobResponse>> StreamDownloadJobsAsync(string baseUrl, string bearerToken, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<IReadOnlyList<ChartHubServerDownloadJobResponse>>();

        public Task<ChartHubServerDownloadJobResponse> RequestInstallDownloadJobAsync(string baseUrl, string bearerToken, Guid jobId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task RequestCancelDownloadJobAsync(string baseUrl, string bearerToken, Guid jobId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<ChartHubServerJobLogEntry>> GetJobLogsAsync(string baseUrl, string bearerToken, Guid jobId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ChartHubServerJobLogEntry>>([]);

        public Task<ChartHubServerVolumeStateResponse> GetVolumeStateAsync(string baseUrl, string bearerToken, CancellationToken cancellationToken = default)
            => Task.FromResult(State);

        public Task<ChartHubServerVolumeActionResponse> SetMasterVolumeAsync(string baseUrl, string bearerToken, int valuePercent, CancellationToken cancellationToken = default)
        {
            LastRequestedMasterVolume = valuePercent;
            State = State with
            {
                Master = new ChartHubServerVolumeMasterResponse(valuePercent, false),
            };

            return Task.FromResult(new ChartHubServerVolumeActionResponse("master", "master", "Master Volume", valuePercent, false, "updated"));
        }

        public Task<ChartHubServerVolumeActionResponse> SetSessionVolumeAsync(string baseUrl, string bearerToken, string sessionId, int valuePercent, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<ChartHubServerVolumeStateResponse> StreamVolumeAsync(string baseUrl, string bearerToken, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return State;
            await Task.CompletedTask;
        }
    }
}