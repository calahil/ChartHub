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
            config.Runtime.DownloadDirectory = "/tmp/downloads";
            config.Runtime.ServerApiAuthToken = "updated-sync-token";
            config.Runtime.RhythmVerseSource = RhythmVerseSource.ChartHubMirror;
        });

        Assert.True(result.IsValid);
        Assert.Equal(1, store.SaveCallCount);
        Assert.Equal("/tmp/downloads", sut.Current.Runtime.DownloadDirectory);
        Assert.Equal("updated-sync-token", sut.Current.Runtime.ServerApiAuthToken);
        Assert.Equal(RhythmVerseSource.ChartHubMirror, sut.Current.Runtime.RhythmVerseSource);
        Assert.NotNull(observed);
        Assert.Equal("/tmp/downloads", observed!.Runtime.DownloadDirectory);
        Assert.Equal("updated-sync-token", observed.Runtime.ServerApiAuthToken);
        Assert.Equal(RhythmVerseSource.ChartHubMirror, observed.Runtime.RhythmVerseSource);
    }

    [Fact]
    public async Task UpdateAsync_WhenValidationFails_DoesNotPersistOrMutateCurrent()
    {
        var store = new InMemoryAppConfigStore();
        var validator = new RejectingValidator("Runtime.DownloadDirectory", "Download path invalid");
        using var sut = new SettingsOrchestrator(store, validator);

        string original = sut.Current.Runtime.DownloadDirectory;
        ConfigValidationResult result = await sut.UpdateAsync(config =>
        {
            config.Runtime.DownloadDirectory = "/invalid";
        });

        Assert.False(result.IsValid);
        Assert.Single(result.Failures);
        Assert.Equal(0, store.SaveCallCount);
        Assert.Equal(original, sut.Current.Runtime.DownloadDirectory);
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
                DownloadDirectory = "/tmp/reloaded",
            },
        });

        AppConfigRoot? observed = null;
        sut.SettingsChanged += config => observed = config;

        await sut.ReloadAsync();

        Assert.Equal("/tmp/reloaded", sut.Current.Runtime.DownloadDirectory);
        Assert.NotNull(observed);
        Assert.Equal("/tmp/reloaded", observed!.Runtime.DownloadDirectory);
    }

    [Fact]
    public async Task UpdateAsync_WithFailedValidation_KeepsOriginalObjectGraphState()
    {
        var store = new InMemoryAppConfigStore();
        var validator = new RejectingValidator("Runtime.OutputDirectory", "Output path invalid");
        using var sut = new SettingsOrchestrator(store, validator);

        string beforeDownloadDir = sut.Current.Runtime.DownloadDirectory;
        string beforeOutputDir = sut.Current.Runtime.OutputDirectory;

        _ = await sut.UpdateAsync(config =>
        {
            config.Runtime.DownloadDirectory = "/tmp/new-downloads";
            config.Runtime.OutputDirectory = "/tmp/new-output";
        });

        Assert.Equal(beforeDownloadDir, sut.Current.Runtime.DownloadDirectory);
        Assert.Equal(beforeOutputDir, sut.Current.Runtime.OutputDirectory);
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
                    TempDirectory = source.Runtime.TempDirectory,
                    DownloadDirectory = source.Runtime.DownloadDirectory,
                    StagingDirectory = source.Runtime.StagingDirectory,
                    OutputDirectory = source.Runtime.OutputDirectory,
                    CloneHeroDataDirectory = source.Runtime.CloneHeroDataDirectory,
                    CloneHeroSongDirectory = source.Runtime.CloneHeroSongDirectory,
                    ServerApiAuthToken = source.Runtime.ServerApiAuthToken,
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
