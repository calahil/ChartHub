using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RhythmVerseClient.Configuration.Interfaces;

namespace RhythmVerseClient.Configuration.Stores;

public class EncryptedFileSecretStore : ISecretStore
{
    private readonly string _secretFilePath;
    private readonly string _masterKeyPath;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public EncryptedFileSecretStore(string secretFilePath, string masterKeyPath)
    {
        _secretFilePath = secretFilePath;
        _masterKeyPath = masterKeyPath;

        var secretDir = Path.GetDirectoryName(secretFilePath);
        if (!string.IsNullOrWhiteSpace(secretDir))
            Directory.CreateDirectory(secretDir);

        var keyDir = Path.GetDirectoryName(masterKeyPath);
        if (!string.IsNullOrWhiteSpace(keyDir))
            Directory.CreateDirectory(keyDir);
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var store = await LoadStoreAsync(cancellationToken);
            return store.TryGetValue(key, out var value) ? value : null;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var store = await LoadStoreAsync(cancellationToken);
            store[key] = value;
            await SaveStoreAsync(store, cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var store = await LoadStoreAsync(cancellationToken);
            if (!store.Remove(key))
                return;

            await SaveStoreAsync(store, cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<bool> ContainsAsync(string key, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var store = await LoadStoreAsync(cancellationToken);
            return store.ContainsKey(key);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<Dictionary<string, string>> LoadStoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_secretFilePath))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        var encryptedPayload = await File.ReadAllTextAsync(_secretFilePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(encryptedPayload))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        var plaintext = Decrypt(encryptedPayload, GetOrCreateMasterKey());
        return JsonSerializer.Deserialize<Dictionary<string, string>>(plaintext)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private async Task SaveStoreAsync(Dictionary<string, string> store, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(store);
        var encryptedPayload = Encrypt(json, GetOrCreateMasterKey());
        await File.WriteAllTextAsync(_secretFilePath, encryptedPayload, cancellationToken);
        TryHardenFilePermissions(_secretFilePath);
    }

    private byte[] GetOrCreateMasterKey()
    {
        if (File.Exists(_masterKeyPath))
        {
            var existing = File.ReadAllText(_masterKeyPath);
            return Convert.FromBase64String(existing);
        }

        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(_masterKeyPath, Convert.ToBase64String(key));
        TryHardenFilePermissions(_masterKeyPath);
        return key;
    }

    private static string Encrypt(string plaintext, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(key, tagSizeInBytes: 16);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var combined = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, combined, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, combined, nonce.Length + tag.Length, ciphertext.Length);

        return Convert.ToBase64String(combined);
    }

    private static string Decrypt(string encryptedPayload, byte[] key)
    {
        var combined = Convert.FromBase64String(encryptedPayload);
        if (combined.Length < 12 + 16)
            throw new InvalidDataException("Invalid encrypted secret payload.");

        var nonce = combined[..12];
        var tag = combined[12..28];
        var ciphertext = combined[28..];
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, tagSizeInBytes: 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }

    private static void TryHardenFilePermissions(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path,
                    UnixFileMode.UserRead |
                    UnixFileMode.UserWrite);
            }
        }
        catch
        {
            // Permission hardening is best-effort only.
        }
    }
}
