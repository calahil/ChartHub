using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;

namespace ChartHub.Configuration.Stores;

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
            {
                candidate.ConfigVersion = AppConfigRoot.CurrentVersion;
            }
        }

        ConfigValidationResult validation = _validator.Validate(candidate);
        if (!validation.IsValid)
        {
            return validation;
        }

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
                RhythmVerseSource = source.Runtime.RhythmVerseSource,
                UseMockData = source.Runtime.UseMockData,
                ServerApiAuthToken = source.Runtime.ServerApiAuthToken,
                ServerApiBaseUrl = source.Runtime.ServerApiBaseUrl,
                InstallLogExpanded = source.Runtime.InstallLogExpanded,
                AndroidVolumeButtonsControlServerVolume = source.Runtime.AndroidVolumeButtonsControlServerVolume,
            },
            GoogleAuth = new GoogleAuthConfig
            {
                AndroidClientId = source.GoogleAuth.AndroidClientId,
                DesktopClientId = source.GoogleAuth.DesktopClientId,
            },
            EncoreUi = new EncoreUiStateConfig
            {
                SearchText = source.EncoreUi.SearchText,
                IsAdvancedVisible = source.EncoreUi.IsAdvancedVisible,
                SelectedInstrument = source.EncoreUi.SelectedInstrument,
                SelectedDifficulty = source.EncoreUi.SelectedDifficulty,
                SelectedDrumType = source.EncoreUi.SelectedDrumType,
                DrumsReviewed = source.EncoreUi.DrumsReviewed,
                SelectedSort = source.EncoreUi.SelectedSort,
                SelectedSortDirection = source.EncoreUi.SelectedSortDirection,
                AdvancedName = source.EncoreUi.AdvancedName,
                AdvancedArtist = source.EncoreUi.AdvancedArtist,
                AdvancedAlbum = source.EncoreUi.AdvancedAlbum,
                AdvancedGenre = source.EncoreUi.AdvancedGenre,
                AdvancedYear = source.EncoreUi.AdvancedYear,
                AdvancedCharter = source.EncoreUi.AdvancedCharter,
                MinYear = source.EncoreUi.MinYear,
                MaxYear = source.EncoreUi.MaxYear,
                MinLength = source.EncoreUi.MinLength,
                MaxLength = source.EncoreUi.MaxLength,
                HasVideoBackground = source.EncoreUi.HasVideoBackground,
                HasLyrics = source.EncoreUi.HasLyrics,
                HasVocals = source.EncoreUi.HasVocals,
                Has2xKick = source.EncoreUi.Has2xKick,
                HasIssues = source.EncoreUi.HasIssues,
                Modchart = source.EncoreUi.Modchart,
            },
        };
    }

    public void Dispose()
    {
        _appConfigStore.ConfigChanged -= OnConfigChanged;
    }
}
