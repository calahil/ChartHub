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
        ValidateHttpUrl(failures, "Runtime.SyncApiDesktopBaseUrl", config.Runtime.SyncApiDesktopBaseUrl, "Desktop Sync API URL", requiresTrailingSlash: false);
        ValidateHttpUrl(failures, "Runtime.SyncApiListenPrefix", config.Runtime.SyncApiListenPrefix, "Desktop Sync listen prefix", requiresTrailingSlash: true);
        ValidateOptionalHttpUrl(failures, "Runtime.SyncApiAdvertisedBaseUrl", config.Runtime.SyncApiAdvertisedBaseUrl, "Desktop Sync advertised URL override", requiresTrailingSlash: false);
        ValidateSyncApiPortCompatibility(failures, config.Runtime.SyncApiListenPrefix, config.Runtime.SyncApiAdvertisedBaseUrl);
        ValidateIntRange(
            failures,
            "Runtime.TransferOrchestratorConcurrencyCap",
            config.Runtime.TransferOrchestratorConcurrencyCap,
            min: 1,
            max: 8,
            label: "Transfer orchestrator concurrency cap");

        return failures.Count == 0 ? ConfigValidationResult.Success : new ConfigValidationResult(failures);
    }

    private static void ValidateDirectoryPath(
        List<ConfigValidationFailure> failures,
        string key,
        string? value,
        string label)
    {
        string path = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            failures.Add(new ConfigValidationFailure(key, $"{label} is required."));
            return;
        }

        if (Uri.TryCreate(path, UriKind.Absolute, out Uri? uri) && !uri.IsFile)
        {
            failures.Add(new ConfigValidationFailure(key, $"{label} must be a local filesystem path."));
            return;
        }

        if (Directory.Exists(path))
        {
            return;
        }

        if (File.Exists(path))
        {
            failures.Add(new ConfigValidationFailure(key, $"{label} points to a file, but a directory is required."));
            return;
        }

        string? parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
        {
            return;
        }

        failures.Add(new ConfigValidationFailure(key, $"{label} is invalid; parent folder does not exist."));
    }

    private static void ValidateHttpUrl(
        List<ConfigValidationFailure> failures,
        string key,
        string? value,
        string label,
        bool requiresTrailingSlash)
    {
        string candidate = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            failures.Add(new ConfigValidationFailure(key, $"{label} is required."));
            return;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            failures.Add(new ConfigValidationFailure(key, $"{label} must be a valid HTTP or HTTPS URL."));
            return;
        }

        if (requiresTrailingSlash && !candidate.EndsWith("/", StringComparison.Ordinal))
        {
            failures.Add(new ConfigValidationFailure(key, $"{label} must end with '/'."));
        }
    }

    private static void ValidateOptionalHttpUrl(
        List<ConfigValidationFailure> failures,
        string key,
        string? value,
        string label,
        bool requiresTrailingSlash)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        ValidateHttpUrl(failures, key, value, label, requiresTrailingSlash);
    }

    private static void ValidateSyncApiPortCompatibility(
        List<ConfigValidationFailure> failures,
        string? listenPrefix,
        string? advertisedBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(advertisedBaseUrl)
            || !Uri.TryCreate(listenPrefix?.Trim(), UriKind.Absolute, out Uri? listenUri)
            || !Uri.TryCreate(advertisedBaseUrl.Trim(), UriKind.Absolute, out Uri? advertisedUri))
        {
            return;
        }

        if (listenUri.Port != advertisedUri.Port)
        {
            failures.Add(new ConfigValidationFailure(
                "Runtime.SyncApiAdvertisedBaseUrl",
                "Desktop Sync advertised URL override must use the same port as the listen prefix."));
        }
    }

    private static void ValidateIntRange(
        List<ConfigValidationFailure> failures,
        string key,
        int value,
        int min,
        int max,
        string label)
    {
        if (value < min || value > max)
        {
            failures.Add(new ConfigValidationFailure(key, $"{label} must be between {min} and {max}."));
        }
    }
}
