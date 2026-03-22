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

    /// <summary>
    /// Applies <paramref name="update"/> to <see cref="Current"/> immediately in memory without
    /// persisting to disk. Use this to make in-memory state visible to readers before the async
    /// disk write completes.
    /// </summary>
    /// <remarks>
    /// This default implementation is not thread-safe. Implementations running in multi-threaded
    /// contexts must override this method and protect <see cref="Current"/> with an appropriate lock.
    /// </remarks>
    void ApplyInMemory(Action<AppConfigRoot> update)
    {
        update(Current);
    }
}
