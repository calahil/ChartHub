using ChartHub.Configuration.Models;

namespace ChartHub.Configuration.Interfaces;

public interface ISettingsOrchestrator
{
    AppConfigRoot Current { get; }

    event Action<AppConfigRoot>? SettingsChanged;

    Task<ConfigValidationResult> UpdateAsync(
        Action<AppConfigRoot> update,
        CancellationToken cancellationToken = default);

    Task ReloadAsync(CancellationToken cancellationToken = default);
}
