using System.Text.Json;

using ChartHub.Configuration.Interfaces;

using Google.Apis.Util.Store;

namespace ChartHub.Services;

public sealed class GoogleAuthTokenDataStore(ISecretStore secretStore, string prefix) : IDataStore
{
    private const string RegistrySuffix = "__keys";

    private readonly ISecretStore _secretStore = secretStore;
    private readonly string _prefix = prefix;
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task StoreAsync<T>(string key, T value)
    {
        ArgumentNullException.ThrowIfNull(value);

        string storageKey = BuildStorageKey(key);
        string payload = JsonSerializer.Serialize(value, _serializerOptions);
        await _secretStore.SetAsync(storageKey, payload).ConfigureAwait(false);

        HashSet<string> keys = await LoadKeysAsync().ConfigureAwait(false);
        if (keys.Add(storageKey))
        {
            await SaveKeysAsync(keys).ConfigureAwait(false);
        }
    }

    public async Task DeleteAsync<T>(string key)
    {
        string storageKey = BuildStorageKey(key);
        await _secretStore.RemoveAsync(storageKey).ConfigureAwait(false);

        HashSet<string> keys = await LoadKeysAsync().ConfigureAwait(false);
        if (keys.Remove(storageKey))
        {
            await SaveKeysAsync(keys).ConfigureAwait(false);
        }
    }

    public async Task<T> GetAsync<T>(string key)
    {
        string? payload = await _secretStore.GetAsync(BuildStorageKey(key)).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return default!;
        }

        return JsonSerializer.Deserialize<T>(payload, _serializerOptions)!;
    }

    public async Task ClearAsync()
    {
        HashSet<string> keys = await LoadKeysAsync().ConfigureAwait(false);
        foreach (string key in keys)
        {
            await _secretStore.RemoveAsync(key).ConfigureAwait(false);
        }

        await _secretStore.RemoveAsync(BuildRegistryKey()).ConfigureAwait(false);
    }

    private string BuildStorageKey(string key) => $"{_prefix}:{key}";

    private string BuildRegistryKey() => $"{_prefix}:{RegistrySuffix}";

    private async Task<HashSet<string>> LoadKeysAsync()
    {
        string? payload = await _secretStore.GetAsync(BuildRegistryKey()).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return JsonSerializer.Deserialize<HashSet<string>>(payload, _serializerOptions)
            ?? new HashSet<string>(StringComparer.Ordinal);
    }

    private Task SaveKeysAsync(HashSet<string> keys)
    {
        if (keys.Count == 0)
        {
            return _secretStore.RemoveAsync(BuildRegistryKey());
        }

        string payload = JsonSerializer.Serialize(keys, _serializerOptions);
        return _secretStore.SetAsync(BuildRegistryKey(), payload);
    }
}