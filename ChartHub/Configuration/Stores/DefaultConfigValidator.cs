using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;

namespace ChartHub.Configuration.Stores;

public sealed class DefaultConfigValidator : IConfigValidator
{
    public ConfigValidationResult Validate(AppConfigRoot config)
    {
        var failures = new List<ConfigValidationFailure>();

        ValidateDirectoryPath(failures, "Runtime.TempDirectory", config.Runtime.TempDirectory, "Temp directory");
        ValidateDirectoryPath(failures, "Runtime.DownloadDirectory", config.Runtime.DownloadDirectory, "Download directory");
        ValidateDirectoryPath(failures, "Runtime.StagingDirectory", config.Runtime.StagingDirectory, "Staging directory");
        ValidateDirectoryPath(failures, "Runtime.OutputDirectory", config.Runtime.OutputDirectory, "Output directory");
        ValidateDirectoryPath(failures, "Runtime.CloneHeroDataDirectory", config.Runtime.CloneHeroDataDirectory, "Clone Hero data directory");
        ValidateDirectoryPath(failures, "Runtime.CloneHeroSongDirectory", config.Runtime.CloneHeroSongDirectory, "Clone Hero song directory");

        return failures.Count == 0 ? ConfigValidationResult.Success : new ConfigValidationResult(failures);
    }

    private static void ValidateDirectoryPath(
        List<ConfigValidationFailure> failures,
        string key,
        string? value,
        string label)
    {
        var path = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            failures.Add(new ConfigValidationFailure(key, $"{label} is required."));
            return;
        }

        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            failures.Add(new ConfigValidationFailure(key, $"{label} must be a local filesystem path."));
            return;
        }

        if (Directory.Exists(path))
            return;

        if (File.Exists(path))
        {
            failures.Add(new ConfigValidationFailure(key, $"{label} points to a file, but a directory is required."));
            return;
        }

        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
            return;

        failures.Add(new ConfigValidationFailure(key, $"{label} is invalid; parent folder does not exist."));
    }
}
