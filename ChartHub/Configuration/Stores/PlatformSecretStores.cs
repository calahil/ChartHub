using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChartHub.Configuration.Interfaces;
using ChartHub.Utilities;

namespace ChartHub.Configuration.Stores;

public sealed class DesktopSecretStore : ISecretStore
{
    private readonly ISecretStore _inner;

    public DesktopSecretBackend Backend { get; }

    public string BackendName => Backend.ToString();

    public DesktopSecretStore(string configRootPath)
    {
        var fileFallback = new EncryptedFileSecretStore(
            Path.Combine(configRootPath, "secrets.store"),
            Path.Combine(configRootPath, "secrets.master.key"));

        if (OperatingSystem.IsWindows())
        {
#if WINDOWS
            _inner = new WindowsDpapiSecretStore(
                Path.Combine(configRootPath, "secrets.dpapi.store"));
            Backend = DesktopSecretBackend.WindowsDpapi;
            Logger.LogInfo("Secrets", "Desktop secret backend selected", new Dictionary<string, object?>
            {
                ["backend"] = BackendName,
            });
            return;
#endif
        }

        if (LinuxSecretToolStore.CanUse())
        {
            _inner = new LinuxSecretToolStore("charthub");
            Backend = DesktopSecretBackend.LinuxSecretService;
            Logger.LogInfo("Secrets", "Desktop secret backend selected", new Dictionary<string, object?>
            {
                ["backend"] = BackendName,
            });
            return;
        }

        _inner = fileFallback;
        Backend = DesktopSecretBackend.EncryptedFileFallback;
        Logger.LogInfo("Secrets", "Desktop secret backend selected", new Dictionary<string, object?>
        {
            ["backend"] = BackendName,
        });
    }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        => _inner.GetAsync(key, cancellationToken);

    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
        => _inner.SetAsync(key, value, cancellationToken);

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        => _inner.RemoveAsync(key, cancellationToken);

    public Task<bool> ContainsAsync(string key, CancellationToken cancellationToken = default)
        => _inner.ContainsAsync(key, cancellationToken);
}

public enum DesktopSecretBackend
{
    LinuxSecretService,
    WindowsDpapi,
    EncryptedFileFallback,
}

#if WINDOWS
internal sealed class WindowsDpapiSecretStore : ISecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ChartHub.SecretStore.v1");

    private readonly string _storagePath;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public WindowsDpapiSecretStore(string storagePath)
    {
        _storagePath = storagePath;
        var directory = Path.GetDirectoryName(storagePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var store = await LoadStoreAsync(cancellationToken).ConfigureAwait(false);
            return store.TryGetValue(key, out var value) ? value : null;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var store = await LoadStoreAsync(cancellationToken).ConfigureAwait(false);
            store[key] = value;
            await SaveStoreAsync(store, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var store = await LoadStoreAsync(cancellationToken).ConfigureAwait(false);
            if (!store.Remove(key))
                return;

            await SaveStoreAsync(store, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<bool> ContainsAsync(string key, CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var store = await LoadStoreAsync(cancellationToken).ConfigureAwait(false);
            return store.ContainsKey(key);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<Dictionary<string, string>> LoadStoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_storagePath))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        var protectedBase64 = await File.ReadAllTextAsync(_storagePath, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(protectedBase64))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        var protectedBytes = Convert.FromBase64String(protectedBase64);
        var plaintextBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        var json = Encoding.UTF8.GetString(plaintextBytes);

        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private async Task SaveStoreAsync(Dictionary<string, string> store, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(store);
        var plaintextBytes = Encoding.UTF8.GetBytes(json);
        var protectedBytes = ProtectedData.Protect(plaintextBytes, Entropy, DataProtectionScope.CurrentUser);
        var payload = Convert.ToBase64String(protectedBytes);
        await File.WriteAllTextAsync(_storagePath, payload, cancellationToken).ConfigureAwait(false);
    }
}
#endif

internal sealed class LinuxSecretToolStore : ISecretStore
{
    private readonly string _applicationName;

    public LinuxSecretToolStore(string applicationName)
    {
        _applicationName = applicationName;
    }

    public static bool CanUse()
    {
        if (!OperatingSystem.IsLinux())
            return false;

        var dbusAddress = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
        if (string.IsNullOrWhiteSpace(dbusAddress))
            return false;

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "secret-tool",
                ArgumentList = { "--help" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null)
                return false;

            process.WaitForExit(1000);
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var result = await RunSecretToolAsync(
            new[] { "lookup", "application", _applicationName, "key", key },
            standardInput: null,
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
            return null;

        return result.StandardOutput.TrimEnd('\r', '\n');
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var result = await RunSecretToolAsync(
            new[]
            {
                "store",
                $"--label={_applicationName}:{key}",
                "application", _applicationName,
                "key", key,
            },
            value,
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
            throw new InvalidOperationException($"secret-tool store failed: {result.StandardError}");
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var result = await RunSecretToolAsync(
            new[] { "clear", "application", _applicationName, "key", key },
            standardInput: null,
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
            throw new InvalidOperationException($"secret-tool clear failed: {result.StandardError}");
    }

    public async Task<bool> ContainsAsync(string key, CancellationToken cancellationToken = default)
    {
        var result = await RunSecretToolAsync(
            new[] { "lookup", "application", _applicationName, "key", key },
            standardInput: null,
            cancellationToken).ConfigureAwait(false);

        return result.ExitCode == 0;
    }

    private static async Task<SecretToolResult> RunSecretToolAsync(
        IReadOnlyList<string> arguments,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "secret-tool",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();

        if (standardInput is not null)
            await process.StandardInput.WriteAsync(standardInput).ConfigureAwait(false);
        process.StandardInput.Close();

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var standardOutput = await stdOutTask.ConfigureAwait(false);
        var standardError = await stdErrTask.ConfigureAwait(false);

        return new SecretToolResult(process.ExitCode, standardOutput, standardError);
    }

    private readonly record struct SecretToolResult(int ExitCode, string StandardOutput, string StandardError);
}

#if ANDROID
public sealed class AndroidSecretStore : AndroidKeystoreSecretStore
{
    public AndroidSecretStore(string configRootPath)
        : base()
    {
    }
}
#else
public sealed class AndroidSecretStore : EncryptedFileSecretStore
{
    public AndroidSecretStore(string configRootPath)
        : base(
            Path.Combine(configRootPath, "secrets.store"),
            Path.Combine(configRootPath, "secrets.master.key"))
    {
    }
}
#endif
