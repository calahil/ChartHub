using RhythmVerseClient.Configuration.Models;

namespace RhythmVerseClient.Configuration.Interfaces;

public interface IConfigValidator
{
    ConfigValidationResult Validate(AppConfigRoot config);
}

public sealed record ConfigValidationFailure(string Key, string Message);

public sealed class ConfigValidationResult
{
    public static readonly ConfigValidationResult Success = new([]);

    public IReadOnlyList<ConfigValidationFailure> Failures { get; }

    public bool IsValid => Failures.Count == 0;

    public ConfigValidationResult(IReadOnlyList<ConfigValidationFailure> failures)
    {
        Failures = failures;
    }
}
