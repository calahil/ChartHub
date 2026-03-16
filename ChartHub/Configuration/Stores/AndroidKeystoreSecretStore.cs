#if ANDROID
using System.Text;
using Android.App;
using Android.Content;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using ChartHub.Configuration.Interfaces;

namespace ChartHub.Configuration.Stores;

/// <summary>
/// Android-only secure secret storage backed by Android Keystore and private SharedPreferences.
/// Secret values are encrypted with AES-GCM using a non-exportable key materialized by the OS keystore.
/// </summary>
public class AndroidKeystoreSecretStore : ISecretStore
{
    private readonly ISharedPreferences _preferences;
    private readonly string _keyAlias;

    public AndroidKeystoreSecretStore(
        string preferencesName = "charthub.secure.settings",
        string keyAlias = "charthub.settings.master")
    {
        var appContext = Application.Context
            ?? throw new InvalidOperationException("Android application context is unavailable.");

        _preferences = appContext.GetSharedPreferences(preferencesName, FileCreationMode.Private)
            ?? throw new InvalidOperationException("Unable to initialize Android secure preferences.");
        _keyAlias = keyAlias;
    }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = _preferences.GetString(key, null);
        if (string.IsNullOrWhiteSpace(payload))
            return Task.FromResult<string?>(null);

        var plaintext = Decrypt(payload);
        return Task.FromResult<string?>(plaintext);
    }

    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = Encrypt(value);
        using var editor = _preferences.Edit()
            ?? throw new InvalidOperationException("Unable to open Android secure preference editor.");
        editor.PutString(key, payload);
        if (!editor.Commit())
            throw new InvalidOperationException($"Failed to persist secret '{key}' in Android secure store.");

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var editor = _preferences.Edit()
            ?? throw new InvalidOperationException("Unable to open Android secure preference editor.");
        editor.Remove(key);
        if (!editor.Commit())
            throw new InvalidOperationException($"Failed to remove secret '{key}' from Android secure store.");

        return Task.CompletedTask;
    }

    public Task<bool> ContainsAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_preferences.Contains(key));
    }

    private string Encrypt(string plaintext)
    {
        var key = GetOrCreateSecretKey();
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")
            ?? throw new InvalidOperationException("Unable to initialize Android AES-GCM cipher.");
        cipher.Init(CipherMode.EncryptMode, key);

        var iv = cipher.GetIV() ?? throw new InvalidOperationException("Android keystore did not provide an IV.");
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = cipher.DoFinal(plaintextBytes)
            ?? throw new InvalidOperationException("Android keystore encryption returned no ciphertext.");

        var envelope = new byte[iv.Length + ciphertext.Length];
        Buffer.BlockCopy(iv, 0, envelope, 0, iv.Length);
        Buffer.BlockCopy(ciphertext, 0, envelope, iv.Length, ciphertext.Length);
        return Convert.ToBase64String(envelope);
    }

    private string Decrypt(string payload)
    {
        var envelope = Convert.FromBase64String(payload);
        if (envelope.Length <= 12)
            throw new InvalidOperationException("Invalid encrypted payload from Android secure store.");

        var iv = envelope[..12];
        var ciphertext = envelope[12..];

        var key = GetOrCreateSecretKey();
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")
            ?? throw new InvalidOperationException("Unable to initialize Android AES-GCM cipher.");
        using var spec = new GCMParameterSpec(128, iv);
        cipher.Init(CipherMode.DecryptMode, key, spec);

        var plaintextBytes = cipher.DoFinal(ciphertext)
            ?? throw new InvalidOperationException("Android keystore decryption returned no plaintext.");
        return Encoding.UTF8.GetString(plaintextBytes);
    }

    private IKey GetOrCreateSecretKey()
    {
        using var keyStore = KeyStore.GetInstance("AndroidKeyStore")
            ?? throw new InvalidOperationException("Unable to access Android keystore.");
        keyStore.Load(null);

        if (!keyStore.ContainsAlias(_keyAlias))
        {
            using var keyGenerator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore")
                ?? throw new InvalidOperationException("Unable to initialize Android keystore key generator.");
            var keySpec = new KeyGenParameterSpec.Builder(
                    _keyAlias,
                    KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
                .SetBlockModes(KeyProperties.BlockModeGcm)
                .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
                .SetRandomizedEncryptionRequired(true)
                .Build();

            keyGenerator.Init(keySpec);
            keyGenerator.GenerateKey();
        }

        return keyStore.GetKey(_keyAlias, null)
            ?? throw new InvalidOperationException("Android keystore key retrieval returned null.");
    }
}
#endif
