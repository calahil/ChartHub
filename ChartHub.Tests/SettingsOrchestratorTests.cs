using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using ChartHub.Configuration.Stores;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public class SettingsOrchestratorTests
{
    [Fact]
    public async Task UpdateAsync_WithValidChanges_PersistsAndRaisesEvent()
    {
        var store = new InMemoryAppConfigStore();
        var validator = new PassThroughValidator();
        using var sut = new SettingsOrchestrator(store, validator);

        AppConfigRoot? observed = null;
        sut.SettingsChanged += config => observed = config;

        ConfigValidationResult result = await sut.UpdateAsync(config =>
        {
            config.Runtime.ServerApiBaseUrl = "https://server.example";
            config.Runtime.ServerApiAuthToken = "updated-sync-token";
            config.Runtime.RhythmVerseSource = RhythmVerseSource.ChartHubMirror;
        });

        Assert.True(result.IsValid);
        Assert.Equal(1, store.SaveCallCount);
        Assert.Equal("https://server.example", sut.Current.Runtime.ServerApiBaseUrl);
        Assert.Equal("updated-sync-token", sut.Current.Runtime.ServerApiAuthToken);
        Assert.Equal(RhythmVerseSource.ChartHubMirror, sut.Current.Runtime.RhythmVerseSource);
        Assert.NotNull(observed);
        Assert.Equal("https://server.example", observed!.Runtime.ServerApiBaseUrl);
        Assert.Equal("updated-sync-token", observed.Runtime.ServerApiAuthToken);
        Assert.Equal(RhythmVerseSource.ChartHubMirror, observed.Runtime.RhythmVerseSource);
    }

    [Fact]
    public async Task UpdateAsync_WhenValidationFails_DoesNotPersistOrMutateCurrent()
    {
        var store = new InMemoryAppConfigStore();
        var validator = new RejectingValidator("Runtime.ServerApiBaseUrl", "Server URL invalid");
        using var sut = new SettingsOrchestrator(store, validator);

        string original = sut.Current.Runtime.ServerApiBaseUrl;
        ConfigValidationResult result = await sut.UpdateAsync(config =>
        {
            config.Runtime.ServerApiBaseUrl = "invalid";
        });

        Assert.False(result.IsValid);
        Assert.Single(result.Failures);
        Assert.Equal(0, store.SaveCallCount);
        Assert.Equal(original, sut.Current.Runtime.ServerApiBaseUrl);
    }

    [Fact]
    public async Task ReloadAsync_LoadsFromStoreAndRaisesEvent()
    {
        var store = new InMemoryAppConfigStore();
        var validator = new PassThroughValidator();
        using var sut = new SettingsOrchestrator(store, validator);

        store.SetNextLoad(new AppConfigRoot
        {
            Runtime = new RuntimeAppConfig
            {
                ServerApiBaseUrl = "https://reloaded.example",
            },
        });

        AppConfigRoot? observed = null;
        sut.SettingsChanged += config => observed = config;

        await sut.ReloadAsync();

        Assert.Equal("https://reloaded.example", sut.Current.Runtime.ServerApiBaseUrl);
        Assert.NotNull(observed);
        Assert.Equal("https://reloaded.example", observed!.Runtime.ServerApiBaseUrl);
    }

    [Fact]
    public async Task UpdateAsync_WithFailedValidation_KeepsOriginalObjectGraphState()
    {
        var store = new InMemoryAppConfigStore();
        var validator = new RejectingValidator("Runtime.ServerApiAuthToken", "Server token invalid");
        using var sut = new SettingsOrchestrator(store, validator);

        string beforeBaseUrl = sut.Current.Runtime.ServerApiBaseUrl;
        string beforeToken = sut.Current.Runtime.ServerApiAuthToken;

        _ = await sut.UpdateAsync(config =>
        {
            config.Runtime.ServerApiBaseUrl = "https://new.example";
            config.Runtime.ServerApiAuthToken = "new-token";
        });

        Assert.Equal(beforeBaseUrl, sut.Current.Runtime.ServerApiBaseUrl);
        Assert.Equal(beforeToken, sut.Current.Runtime.ServerApiAuthToken);
        Assert.Equal(0, store.SaveCallCount);
    }

    private sealed class InMemoryAppConfigStore : IAppConfigStore
    {
        private AppConfigRoot _current = new();
        private AppConfigRoot? _nextLoad;

        public string ConfigPath => "/tmp/in-memory-config.json";

        public int SaveCallCount { get; private set; }

        public event Action<AppConfigRoot>? ConfigChanged;

        public AppConfigRoot Load()
        {
            if (_nextLoad is not null)
            {
                _current = Clone(_nextLoad);
                _nextLoad = null;
            }

            return Clone(_current);
        }

        public Task SaveAsync(AppConfigRoot config, CancellationToken cancellationToken = default)
        {
            SaveCallCount++;
            _current = Clone(config);
            ConfigChanged?.Invoke(Clone(_current));
            return Task.CompletedTask;
        }

        public void SetNextLoad(AppConfigRoot config)
        {
            _nextLoad = Clone(config);
        }

        public void Dispose()
        {
        }

        private static AppConfigRoot Clone(AppConfigRoot source)
        {
            return new AppConfigRoot
            {
                ConfigVersion = source.ConfigVersion,
                Runtime = new RuntimeAppConfig
                {
                    RhythmVerseSource = source.Runtime.RhythmVerseSource,
                    UseMockData = source.Runtime.UseMockData,
                    ServerApiAuthToken = source.Runtime.ServerApiAuthToken,
                    ServerApiBaseUrl = source.Runtime.ServerApiBaseUrl,
                    InstallLogExpanded = source.Runtime.InstallLogExpanded,
                },
                GoogleAuth = new GoogleAuthConfig
                {
                    AndroidClientId = source.GoogleAuth.AndroidClientId,
                    DesktopClientId = source.GoogleAuth.DesktopClientId,
                },
            };
        }
    }

    private sealed class PassThroughValidator : IConfigValidator
    {
        public ConfigValidationResult Validate(AppConfigRoot config)
        {
            return ConfigValidationResult.Success;
        }
    }

    private sealed class RejectingValidator(string key, string message) : IConfigValidator
    {
        private readonly string _key = key;
        private readonly string _message = message;

        public ConfigValidationResult Validate(AppConfigRoot config)
        {
            return new ConfigValidationResult([
                new ConfigValidationFailure(_key, _message),
            ]);
        }
    }
}
