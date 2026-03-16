using System.Text.Json;
using System.Text.Json.Nodes;
using RhythmVerseClient.Configuration.Interfaces;
using RhythmVerseClient.Configuration.Models;
using RhythmVerseClient.Configuration.Secrets;

namespace RhythmVerseClient.Configuration.Migration;

public sealed class SettingsMigrationService(IAppConfigStore appConfigStore, ISecretStore secretStore)
{
    private readonly IAppConfigStore _appConfigStore = appConfigStore;
    private readonly ISecretStore _secretStore = secretStore;

    public async Task<SettingsMigrationResult> MigrateLegacySecretsAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_appConfigStore.ConfigPath))
            return SettingsMigrationResult.None;

        JsonObject? root;
        try
        {
            root = JsonNode.Parse(await File.ReadAllTextAsync(_appConfigStore.ConfigPath, cancellationToken)) as JsonObject;
        }
        catch
        {
            return SettingsMigrationResult.None;
        }

        if (root is null)
            return SettingsMigrationResult.None;

        var movedKeys = new List<string>();
        if (root["GoogleDrive"] is JsonObject googleDrive)
        {
            await MoveIfPresentAsync(googleDrive, "client_secret", SecretKeys.GoogleDesktopClientSecret, movedKeys, cancellationToken);
            await MoveIfPresentAsync(googleDrive, "desktop_client_secret", SecretKeys.GoogleDesktopClientSecret, movedKeys, cancellationToken);
            await MoveIfPresentAsync(googleDrive, "refresh_token", SecretKeys.GoogleRefreshToken, movedKeys, cancellationToken);
            await MoveIfPresentAsync(googleDrive, "access_token", SecretKeys.GoogleAccessToken, movedKeys, cancellationToken);
        }

        var hasVersionNode = root["ConfigVersion"] is not null;
        if (!hasVersionNode)
            root["ConfigVersion"] = AppConfigRoot.CurrentVersion;

        if (movedKeys.Count == 0 && hasVersionNode)
            return SettingsMigrationResult.None;

        var backupPath = $"{_appConfigStore.ConfigPath}.bak";
        if (!File.Exists(backupPath))
            File.Copy(_appConfigStore.ConfigPath, backupPath);

        await File.WriteAllTextAsync(
            _appConfigStore.ConfigPath,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        return new SettingsMigrationResult(movedKeys, backupPath);
    }

    private async Task MoveIfPresentAsync(
        JsonObject sourceObject,
        string legacyKey,
        string secretKey,
        List<string> movedKeys,
        CancellationToken cancellationToken)
    {
        var secretValue = sourceObject[legacyKey]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(secretValue))
            return;

        await _secretStore.SetAsync(secretKey, secretValue, cancellationToken);
        sourceObject.Remove(legacyKey);
        movedKeys.Add(secretKey);
    }
}

public sealed record SettingsMigrationResult(IReadOnlyList<string> MovedSecretKeys, string? BackupPath)
{
    public static readonly SettingsMigrationResult None = new([], null);

    public bool HasChanges => MovedSecretKeys.Count > 0 || !string.IsNullOrWhiteSpace(BackupPath);
}
