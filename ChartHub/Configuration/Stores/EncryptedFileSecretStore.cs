using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using ChartHub.Configuration.Interfaces;

namespace ChartHub.Configuration.Stores;

public class EncryptedFileSecretStore : ISecretStore
{
    private readonly string _secretFilePath;
    private readonly string _masterKeyPath;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public EncryptedFileSecretStore(string secretFilePath, string masterKeyPath)
    {
        _secretFilePath = secretFilePath;
        _masterKeyPath = masterKeyPath;

        string? secretDir = Path.GetDirectoryName(secretFilePath);
        if (!string.IsNullOrWhiteSpace(secretDir))
        {
            Directory.CreateDirectory(secretDir);
        }

        string? keyDir = Path.GetDirectoryName(masterKeyPath);
        if (!string.IsNullOrWhiteSpace(keyDir))
        {
            Directory.CreateDirectory(keyDir);
        }
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            Dictionary<string, string> store = await LoadStoreAsync(cancellationToken);
            return store.TryGetValue(key, out string? value) ? value : null;
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
            Dictionary<string, string> store = await LoadStoreAsync(cancellationToken);
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
            Dictionary<string, string> store = await LoadStoreAsync(cancellationToken);
            if (!store.Remove(key))
            {
                return;
            }

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
            Dictionary<string, string> store = await LoadStoreAsync(cancellationToken);
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
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        string encryptedPayload = await File.ReadAllTextAsync(_secretFilePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(encryptedPayload))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        string plaintext = Decrypt(encryptedPayload, GetOrCreateMasterKey());
        return JsonSerializer.Deserialize<Dictionary<string, string>>(plaintext)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private async Task SaveStoreAsync(Dictionary<string, string> store, CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(store);
        string encryptedPayload = Encrypt(json, GetOrCreateMasterKey());
        await File.WriteAllTextAsync(_secretFilePath, encryptedPayload, cancellationToken);
        TryHardenFilePermissions(_secretFilePath);
    }

    private byte[] GetOrCreateMasterKey()
    {
        if (File.Exists(_masterKeyPath))
        {
            string existing = File.ReadAllText(_masterKeyPath);
            return Convert.FromBase64String(existing);
        }

        byte[] key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(_masterKeyPath, Convert.ToBase64String(key));
        TryHardenFilePermissions(_masterKeyPath);
        return key;
    }

    private static string Encrypt(string plaintext, byte[] key)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] ciphertext = new byte[plaintextBytes.Length];
        byte[] tag = new byte[16];

        using var aes = new AesGcm(key, tagSizeInBytes: 16);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        byte[] combined = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, combined, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, combined, nonce.Length + tag.Length, ciphertext.Length);

        return Convert.ToBase64String(combined);
    }

    private static string Decrypt(string encryptedPayload, byte[] key)
    {
        byte[] combined = Convert.FromBase64String(encryptedPayload);
        if (combined.Length < 12 + 16)
        {
            throw new InvalidDataException("Invalid encrypted secret payload.");
        }

        byte[] nonce = combined[..12];
        byte[] tag = combined[12..28];
        byte[] ciphertext = combined[28..];
        byte[] plaintext = new byte[ciphertext.Length];

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
