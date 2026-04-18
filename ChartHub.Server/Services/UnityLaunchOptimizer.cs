using ChartHub.Server.Options;

using Microsoft.Extensions.Options;

namespace ChartHub.Server.Services;

public sealed partial class UnityLaunchOptimizer(
    IOptions<UnityLaunchOptions> options,
    ILogger<UnityLaunchOptimizer> logger) : IUnityLaunchOptimizer
{
    private readonly UnityLaunchOptions _options = options.Value;
    private readonly ILogger<UnityLaunchOptimizer> _logger = logger;

    public async Task<IReadOnlyDictionary<string, string>> OptimizeAsync(
        string executablePath,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return ImmutableEmptyDictionary;
        }

        string? bootConfigPath = FindBootConfigPath(executablePath);
        if (bootConfigPath is null)
        {
            return ImmutableEmptyDictionary;
        }

        await PatchBootConfigAsync(bootConfigPath, cancellationToken).ConfigureAwait(false);

        return _options.EnvironmentVariables.Count == 0
            ? ImmutableEmptyDictionary
            : _options.EnvironmentVariables;
    }

    internal static string? FindBootConfigPath(string executablePath)
    {
        string? execDir = Path.GetDirectoryName(executablePath);
        if (execDir is null || !Directory.Exists(execDir))
        {
            return null;
        }

        foreach (string subDir in Directory.EnumerateDirectories(execDir, "*_Data", SearchOption.TopDirectoryOnly))
        {
            string candidate = Path.Combine(subDir, "boot.config");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private async Task PatchBootConfigAsync(string bootConfigPath, CancellationToken cancellationToken)
    {
        if (_options.BootConfig.Count == 0)
        {
            return;
        }

        string existing = File.Exists(bootConfigPath)
            ? await File.ReadAllTextAsync(bootConfigPath, cancellationToken).ConfigureAwait(false)
            : string.Empty;

        string patched = UpsertBootConfigKeys(existing, _options.BootConfig);

        if (string.Equals(patched, existing, StringComparison.Ordinal))
        {
            return;
        }

        await File.WriteAllTextAsync(bootConfigPath, patched, cancellationToken).ConfigureAwait(false);
        LogBootConfigPatched(_logger, bootConfigPath);
    }

    [LoggerMessage(
        EventId = 7200,
        Level = LogLevel.Information,
        Message = "Patched Unity boot.config at {Path}")]
    private static partial void LogBootConfigPatched(ILogger logger, string path);

    internal static string UpsertBootConfigKeys(string content, IReadOnlyDictionary<string, string> keys)
    {
        // Split into non-empty lines for processing; preserve trailing newline convention.
        var lines = content
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .ToList();

        // Remove the synthetic empty string that Split adds after a trailing newline.
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var satisfied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            int eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                continue;
            }

            string lineKey = line[..eq];
            if (!keys.TryGetValue(lineKey, out string? desiredValue))
            {
                continue;
            }

            string currentValue = line[(eq + 1)..];
            if (!string.Equals(currentValue, desiredValue, StringComparison.Ordinal))
            {
                lines[i] = $"{lineKey}={desiredValue}";
            }

            satisfied.Add(lineKey);
        }

        // Append any keys that were not present at all.
        foreach ((string key, string value) in keys)
        {
            if (!satisfied.Contains(key))
            {
                lines.Add($"{key}={value}");
            }
        }

        return string.Join('\n', lines) + '\n';
    }

    private static readonly IReadOnlyDictionary<string, string> ImmutableEmptyDictionary =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
