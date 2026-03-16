using RhythmVerseClient.Configuration.Models;

namespace RhythmVerseClient.Configuration.Interfaces;

public interface IAppConfigStore : IDisposable
{
    string ConfigPath { get; }

    AppConfigRoot Load();

    Task SaveAsync(AppConfigRoot config, CancellationToken cancellationToken = default);

    event Action<AppConfigRoot>? ConfigChanged;
}
