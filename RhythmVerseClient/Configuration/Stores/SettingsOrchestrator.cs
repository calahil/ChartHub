using RhythmVerseClient.Configuration.Interfaces;
using RhythmVerseClient.Configuration.Models;

namespace RhythmVerseClient.Configuration.Stores;

public sealed class SettingsOrchestrator : ISettingsOrchestrator, IDisposable
{
    private readonly IAppConfigStore _appConfigStore;
    private readonly IConfigValidator _validator;
    private readonly object _sync = new();

    public AppConfigRoot Current { get; private set; }

    public event Action<AppConfigRoot>? SettingsChanged;

    public SettingsOrchestrator(IAppConfigStore appConfigStore, IConfigValidator validator)
    {
        _appConfigStore = appConfigStore;
        _validator = validator;
        Current = _appConfigStore.Load();
        _appConfigStore.ConfigChanged += OnConfigChanged;
    }

    public async Task<ConfigValidationResult> UpdateAsync(
        Action<AppConfigRoot> update,
        CancellationToken cancellationToken = default)
    {
        AppConfigRoot candidate;
        lock (_sync)
        {
            candidate = Clone(Current);
            update(candidate);
            if (candidate.ConfigVersion <= 0)
                candidate.ConfigVersion = AppConfigRoot.CurrentVersion;
        }

        var validation = _validator.Validate(candidate);
        if (!validation.IsValid)
            return validation;

        await _appConfigStore.SaveAsync(candidate, cancellationToken).ConfigureAwait(false);

        lock (_sync)
        {
            Current = candidate;
        }

        SettingsChanged?.Invoke(Current);
        return ConfigValidationResult.Success;
    }

    public Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            Current = _appConfigStore.Load();
        }

        SettingsChanged?.Invoke(Current);
        return Task.CompletedTask;
    }

    private void OnConfigChanged(AppConfigRoot config)
    {
        lock (_sync)
        {
            Current = config;
        }

        SettingsChanged?.Invoke(Current);
    }

    private static AppConfigRoot Clone(AppConfigRoot source)
    {
        return new AppConfigRoot
        {
            ConfigVersion = source.ConfigVersion,
            Runtime = new RuntimeAppConfig
            {
                UseMockData = source.Runtime.UseMockData,
                TempDirectory = source.Runtime.TempDirectory,
                DownloadDirectory = source.Runtime.DownloadDirectory,
                StagingDirectory = source.Runtime.StagingDirectory,
                OutputDirectory = source.Runtime.OutputDirectory,
                CloneHeroDataDirectory = source.Runtime.CloneHeroDataDirectory,
                CloneHeroSongDirectory = source.Runtime.CloneHeroSongDirectory,
            },
            GoogleAuth = new GoogleAuthConfig
            {
                AndroidClientId = source.GoogleAuth.AndroidClientId,
                DesktopClientId = source.GoogleAuth.DesktopClientId,
            },
        };
    }

    public void Dispose()
    {
        _appConfigStore.ConfigChanged -= OnConfigChanged;
    }
}
