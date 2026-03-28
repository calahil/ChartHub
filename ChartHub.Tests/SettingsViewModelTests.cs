using System.Reflection;

using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using ChartHub.Configuration.Secrets;
using ChartHub.Services;
using ChartHub.Tests.TestInfrastructure;
using ChartHub.ViewModels;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public class SettingsViewModelTests
{
    [Fact]
    public async Task Constructor_BuildsFieldsFromMetadataAndLoadsSecrets()
    {
        using var temp = new TemporaryDirectoryFixture("settings-vm-fields");
        var orchestrator = new FakeSettingsOrchestrator(CreateConfig(temp.RootPath));
        var secrets = new InMemorySecretStore();
        var cloudAccount = new FakeCloudStorageAccountService();
        await secrets.SetAsync(SecretKeys.GoogleRefreshToken, "stored-refresh-token");

        using SettingsViewModel sut = CreateSettingsViewModel(orchestrator, secrets, cloudAccount);
        await Task.Yield();

        Assert.Equal(21, sut.Fields.Count);
        Assert.Contains(sut.Fields, field => field.Key == "Runtime.RhythmVerseSource" && field.IsDropdownEditor);
        Assert.Contains(sut.Fields, field => field.Key == "Runtime.UseMockData" && field.IsToggleEditor);
        Assert.Contains(sut.Fields, field => field.Key == "Runtime.DownloadDirectory" && field.IsDirectoryPicker);
        Assert.DoesNotContain(sut.Fields, field => field.Key == "Runtime.SyncApiDesktopBaseUrl");
        Assert.DoesNotContain(sut.Fields, field => field.Key == "Runtime.SyncApiListenPrefix");
        Assert.DoesNotContain(sut.Fields, field => field.Key == "Runtime.SyncApiAdvertisedBaseUrl");
        Assert.Contains(sut.Fields, field => field.Key == "Runtime.SyncApiDeviceLabel" && field.IsTextEditor);
        Assert.Contains(sut.Fields, field => field.Key == "Runtime.SyncApiPairCode" && field.IsTextEditor);
        Assert.DoesNotContain(sut.Fields, field => field.Key == "Runtime.SyncApiPairCodeIssuedAtUtc");
        Assert.Contains(sut.Fields, field => field.Key == "Runtime.SyncApiPairCodeTtlMinutes" && field.IsNumberEditor);
        Assert.DoesNotContain(sut.Fields, field => field.Key == "Runtime.SyncApiSavedConnectionsJson");
        Assert.Contains(sut.Fields, field => field.Key == "Runtime.SyncApiMaxRequestBodyBytes" && field.IsNumberEditor);
        Assert.Contains(sut.Fields, field => field.Key == "Runtime.SyncApiBodyReadTimeoutMs" && field.IsNumberEditor);
        Assert.Contains(sut.Fields, field => field.Key == "Runtime.SyncApiMutationWaitTimeoutMs" && field.IsNumberEditor);
        Assert.Contains(sut.Fields, field => field.Key == "Runtime.SyncApiSlowRequestThresholdMs" && field.IsNumberEditor);
        Assert.Contains(sut.Fields, field => field.Key == "Runtime.TransferOrchestratorConcurrencyCap" && field.IsNumberEditor);
        Assert.Contains(sut.Fields, field => field.Key == "GoogleAuth.AndroidClientId" && field.IsTextEditor);
        Assert.Contains(sut.Fields, field => field.IsGroupHeaderVisible);
        int expectedSecretCount = sut.IsDeveloperBuild ? 3 : 0;
        Assert.Equal(expectedSecretCount, sut.Secrets.Count);
        if (sut.IsDeveloperBuild)
        {
            Assert.Contains(sut.Secrets, secret => secret.Key == SecretKeys.GoogleRefreshToken && secret.HasStoredValue);
        }
    }

    [Fact]
    public async Task Constructor_OnAndroid_ShowsAndroidSyncSettingsAndHidesDesktopHostSyncSettings()
    {
        using var temp = new TemporaryDirectoryFixture("settings-vm-platform-android");
        var orchestrator = new FakeSettingsOrchestrator(CreateConfig(temp.RootPath));
        var secrets = new InMemorySecretStore();
        var cloudAccount = new FakeCloudStorageAccountService();

        using SettingsViewModel sut = CreateSettingsViewModel(orchestrator, secrets, cloudAccount, isAndroidPlatform: true);
        await Task.Yield();

        Assert.DoesNotContain(sut.Fields, field => field.Key == "Runtime.SyncApiDesktopBaseUrl");
        Assert.DoesNotContain(sut.Fields, field => field.Key == "Runtime.SyncApiListenPrefix");
        Assert.DoesNotContain(sut.Fields, field => field.Key == "Runtime.SyncApiAdvertisedBaseUrl");
        Assert.DoesNotContain(sut.Fields, field => field.Key == "Runtime.AllowSyncApiStateOverride");
        Assert.DoesNotContain(sut.Fields, field => field.Key == "Runtime.SyncApiMaxRequestBodyBytes");
    }

    [Fact]
    public async Task DirectoryField_InvalidPath_SetsValidationErrorAndDisablesSave()
    {
        using var temp = new TemporaryDirectoryFixture("settings-vm-validation");
        var orchestrator = new FakeSettingsOrchestrator(CreateConfig(temp.RootPath));
        var secrets = new InMemorySecretStore();
        var cloudAccount = new FakeCloudStorageAccountService();

        using SettingsViewModel sut = CreateSettingsViewModel(orchestrator, secrets, cloudAccount);
        await Task.Yield();

        SettingsFieldViewModel field = Assert.Single(sut.Fields, item => item.Key == "Runtime.DownloadDirectory");
        field.StringValue = "https://example.com/downloads";

        Assert.True(field.HasError);
        Assert.Equal("Path must be a local filesystem path.", field.ErrorMessage);
        Assert.True(sut.HasValidationErrors);
        Assert.False(sut.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task SaveCommand_WithValidChanges_PersistsAndSetsSavedStatus()
    {
        using var temp = new TemporaryDirectoryFixture("settings-vm-save");
        var orchestrator = new FakeSettingsOrchestrator(CreateConfig(temp.RootPath));
        var secrets = new InMemorySecretStore();
        var cloudAccount = new FakeCloudStorageAccountService();

        using SettingsViewModel sut = CreateSettingsViewModel(orchestrator, secrets, cloudAccount);
        await Task.Yield();

        SettingsFieldViewModel androidClientField = Assert.Single(sut.Fields, item => item.Key == "GoogleAuth.AndroidClientId");
        androidClientField.StringValue = "android-client-updated";

        Assert.True(sut.SaveCommand.CanExecute(null));
        await sut.SaveCommand.ExecuteAsync(null);

        Assert.Equal(1, orchestrator.UpdateCallCount);
        Assert.Equal("android-client-updated", orchestrator.Current.GoogleAuth.AndroidClientId);
        Assert.Equal("Settings saved.", sut.StatusMessage);
    }

    [Fact]
    public async Task SaveCommand_WithNonHotReloadableChanges_PersistsAndRunsReloadFlow()
    {
        using var temp = new TemporaryDirectoryFixture("settings-vm-save-reload");
        var orchestrator = new FakeSettingsOrchestrator(CreateConfig(temp.RootPath));
        var secrets = new InMemorySecretStore();
        var cloudAccount = new FakeCloudStorageAccountService();

        using SettingsViewModel sut = CreateSettingsViewModel(orchestrator, secrets, cloudAccount);
        await Task.Yield();

        SettingsFieldViewModel tempDirectoryField = Assert.Single(sut.Fields, item => item.Key == "Runtime.TempDirectory");
        tempDirectoryField.StringValue = Path.Combine(temp.RootPath, "Temp-New");

        Assert.True(sut.HasPendingRestartSettings);
        await sut.SaveCommand.ExecuteAsync(null);

        Assert.Equal(1, orchestrator.UpdateCallCount);
        Assert.Equal(1, orchestrator.ReloadCallCount);
        Assert.Equal("Settings saved and reloaded from current configuration.", sut.StatusMessage);
        Assert.False(sut.HasPendingRestartSettings);
    }

    [Fact]
    public async Task HasPendingRestartSettings_OnlyTrueWhenNonHotReloadableFieldIsDirty()
    {
        using var temp = new TemporaryDirectoryFixture("settings-vm-reload-indicator");
        var orchestrator = new FakeSettingsOrchestrator(CreateConfig(temp.RootPath));
        var secrets = new InMemorySecretStore();
        var cloudAccount = new FakeCloudStorageAccountService();

        using SettingsViewModel sut = CreateSettingsViewModel(orchestrator, secrets, cloudAccount);
        await Task.Yield();

        Assert.False(sut.HasPendingRestartSettings);

        SettingsFieldViewModel hotReloadableField = Assert.Single(sut.Fields, item => item.Key == "GoogleAuth.AndroidClientId");
        hotReloadableField.StringValue = "android-client-changed";
        Assert.False(sut.HasPendingRestartSettings);

        SettingsFieldViewModel nonHotField = Assert.Single(sut.Fields, item => item.Key == "Runtime.StagingDirectory");
        nonHotField.StringValue = Path.Combine(temp.RootPath, "Staging-New");
        Assert.True(sut.HasPendingRestartSettings);
    }

    [Fact]
    public async Task SecretCommands_SaveAndClear_UpdateStateAndStore()
    {
        using var temp = new TemporaryDirectoryFixture("settings-vm-secrets");
        var orchestrator = new FakeSettingsOrchestrator(CreateConfig(temp.RootPath));
        var secrets = new InMemorySecretStore();
        var cloudAccount = new FakeCloudStorageAccountService();

        using SettingsViewModel sut = CreateSettingsViewModel(orchestrator, secrets, cloudAccount);
        await Task.Yield();

        if (!sut.IsDeveloperBuild)
        {
            Assert.Empty(sut.Secrets);
            return;
        }

        SecretFieldViewModel secret = Assert.Single(sut.Secrets, item => item.Key == SecretKeys.GoogleDesktopClientSecret);
        secret.Value = " desktop-secret-value ";

        await sut.SaveSecretCommand.ExecuteAsync(secret);

        Assert.True(secret.HasStoredValue);
        Assert.Equal(string.Empty, secret.Value);
        Assert.Equal("Saved secret: Google Desktop Client Secret", sut.StatusMessage);
        Assert.Equal("desktop-secret-value", await secrets.GetAsync(SecretKeys.GoogleDesktopClientSecret));

        await sut.ClearSecretCommand.ExecuteAsync(secret);

        Assert.False(secret.HasStoredValue);
        Assert.Equal("Cleared secret: Google Desktop Client Secret", sut.StatusMessage);
        Assert.Null(await secrets.GetAsync(SecretKeys.GoogleDesktopClientSecret));
    }

    [Fact]
    public async Task CloudAccountCommands_LinkAndUnlink_UpdateStateAndCallService()
    {
        using var temp = new TemporaryDirectoryFixture("settings-vm-cloud-account");
        var orchestrator = new FakeSettingsOrchestrator(CreateConfig(temp.RootPath));
        var secrets = new InMemorySecretStore();
        var cloudAccount = new FakeCloudStorageAccountService
        {
            TryRestoreSessionResult = false,
        };

        using SettingsViewModel sut = CreateSettingsViewModel(orchestrator, secrets, cloudAccount);
        await Task.Yield();

        Assert.False(sut.IsCloudAccountLinked);
        Assert.Equal("Google Drive", sut.CloudProviderDisplayName);
        Assert.Equal("google-drive", sut.CloudProviderId);
        Assert.Equal("Google Drive is not linked.", sut.CloudAccountStatusMessage);

        await sut.LinkCloudAccountCommand.ExecuteAsync(null);

        Assert.True(sut.IsCloudAuthGateVisible);
        Assert.NotNull(sut.CloudAuthGateViewModel);
        Assert.Equal(0, cloudAccount.LinkCallCount);

        await sut.CloudAuthGateViewModel!.SignInCommand.ExecuteAsync(null);

        Assert.Equal(1, cloudAccount.LinkCallCount);
        Assert.True(sut.IsCloudAccountLinked);
        Assert.Equal("Google Drive linked.", sut.CloudAccountStatusMessage);
        Assert.False(sut.IsCloudAuthGateVisible);

        await sut.UnlinkCloudAccountCommand.ExecuteAsync(null);

        Assert.Equal(1, cloudAccount.UnlinkCallCount);
        Assert.False(sut.IsCloudAccountLinked);
        Assert.Equal("Google Drive is not linked.", sut.CloudAccountStatusMessage);
    }

    [Fact]
    public async Task CloudAccountCommands_WhenAuthGateDismissed_RemainsUnlinkedWithoutCallingLink()
    {
        using var temp = new TemporaryDirectoryFixture("settings-vm-cloud-dismiss");
        var orchestrator = new FakeSettingsOrchestrator(CreateConfig(temp.RootPath));
        var secrets = new InMemorySecretStore();
        var cloudAccount = new FakeCloudStorageAccountService
        {
            TryRestoreSessionResult = false,
        };

        using SettingsViewModel sut = CreateSettingsViewModel(orchestrator, secrets, cloudAccount);
        await Task.Yield();

        await sut.LinkCloudAccountCommand.ExecuteAsync(null);

        Assert.True(sut.IsCloudAuthGateVisible);
        Assert.Equal(0, cloudAccount.LinkCallCount);

        sut.DismissCloudAuthGateCommand.Execute(null);

        Assert.False(sut.IsCloudAuthGateVisible);
        Assert.False(sut.IsCloudAccountLinked);
        Assert.Equal(0, cloudAccount.LinkCallCount);
        Assert.Equal("Google Drive is not linked.", sut.CloudAccountStatusMessage);
    }

    [Fact]
    public async Task Constructor_WhenCloudAlreadyLinked_SetsLinkedStatusAndNoHintError()
    {
        using var temp = new TemporaryDirectoryFixture("settings-vm-cloud-linked");
        var orchestrator = new FakeSettingsOrchestrator(CreateConfig(temp.RootPath));
        var secrets = new InMemorySecretStore();
        var cloudAccount = new FakeCloudStorageAccountService
        {
            TryRestoreSessionResult = true,
        };

        using SettingsViewModel sut = CreateSettingsViewModel(orchestrator, secrets, cloudAccount);
        await Task.Yield();

        Assert.True(sut.IsCloudAccountLinked);
        Assert.Equal("Google Drive linked.", sut.CloudAccountStatusMessage);
        Assert.False(sut.HasCloudAccountError);
    }

    [Fact]
    public async Task Constructor_WithPairingHistory_PopulatesRecentPairings()
    {
        using var temp = new TemporaryDirectoryFixture("settings-vm-pairing-history");
        AppConfigRoot config = CreateConfig(temp.RootPath);
        config.Runtime.SyncApiPairingHistoryJson =
            "[{\"deviceLabel\":\"Pixel 9\",\"pairedAtUtc\":\"2026-01-02T03:04:05Z\"},{\"deviceLabel\":\"Galaxy S24\",\"pairedAtUtc\":\"2026-01-01T01:02:03Z\"}]";

        var orchestrator = new FakeSettingsOrchestrator(config);
        var secrets = new InMemorySecretStore();
        var cloudAccount = new FakeCloudStorageAccountService();

        using SettingsViewModel sut = CreateSettingsViewModel(orchestrator, secrets, cloudAccount);
        await Task.Yield();

        Assert.True(sut.HasPairingHistory);
        Assert.False(sut.HasNoPairingHistory);
        Assert.Equal(2, sut.PairingHistoryEntries.Count);
        Assert.Equal("Pixel 9", sut.PairingHistoryEntries[0].DeviceLabel);
        Assert.Equal("Galaxy S24", sut.PairingHistoryEntries[1].DeviceLabel);
    }

    [Fact]
    public async Task Constructor_WithMalformedPairingHistory_UsesEmptyHistory()
    {
        using var temp = new TemporaryDirectoryFixture("settings-vm-pairing-history-malformed");
        AppConfigRoot config = CreateConfig(temp.RootPath);
        config.Runtime.SyncApiPairingHistoryJson = "{not-json";

        var orchestrator = new FakeSettingsOrchestrator(config);
        var secrets = new InMemorySecretStore();
        var cloudAccount = new FakeCloudStorageAccountService();

        using SettingsViewModel sut = CreateSettingsViewModel(orchestrator, secrets, cloudAccount);
        await Task.Yield();

        Assert.False(sut.HasPairingHistory);
        Assert.True(sut.HasNoPairingHistory);
        Assert.Empty(sut.PairingHistoryEntries);
    }

    [Fact]
    public async Task ApplySyncEndpointCommand_PersistsPreferredEndpoint()
    {
        using var temp = new TemporaryDirectoryFixture("settings-vm-preferred-sync-endpoint");
        AppConfigRoot config = CreateConfig(temp.RootPath);
        config.Runtime.SyncApiSavedConnectionsJson =
            "[{\"apiBaseUrl\":\"http://192.168.1.44:15123\"},{\"apiBaseUrl\":\"http://192.168.1.55:15123\"}]";

        var orchestrator = new FakeSettingsOrchestrator(config);
        var secrets = new InMemorySecretStore();
        var cloudAccount = new FakeCloudStorageAccountService();

        using SettingsViewModel sut = CreateSettingsViewModel(orchestrator, secrets, cloudAccount);
        await Task.Yield();

        Assert.True(sut.HasSyncEndpointOptions);
        Assert.Equal(2, sut.SyncEndpointOptions.Count);
        Assert.Contains("http://192.168.1.44:15123", sut.SyncEndpointOptions);
        Assert.Contains("http://192.168.1.55:15123", sut.SyncEndpointOptions);
        Assert.Equal("http://192.168.1.44:15123", sut.SelectedSyncEndpoint);

        sut.SelectedSyncEndpoint = "http://192.168.1.55:15123";
        await sut.ApplySyncEndpointCommand.ExecuteAsync(null);

        Assert.Equal("http://192.168.1.55:15123", orchestrator.Current.Runtime.SyncApiPreferredBaseUrl);
        Assert.Equal("Preferred sync endpoint updated.", sut.StatusMessage);
    }

    private static AppConfigRoot CreateConfig(string rootPath)
    {
        string tempDirectory = Path.Combine(rootPath, "Temp");
        string downloadDirectory = Path.Combine(rootPath, "Downloads");
        string stagingDirectory = Path.Combine(rootPath, "Staging");
        string outputDirectory = Path.Combine(rootPath, "Output");
        string cloneHeroDataDirectory = Path.Combine(rootPath, "CloneHero");
        string cloneHeroSongDirectory = Path.Combine(cloneHeroDataDirectory, "Songs");

        Directory.CreateDirectory(tempDirectory);
        Directory.CreateDirectory(downloadDirectory);
        Directory.CreateDirectory(stagingDirectory);
        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(cloneHeroDataDirectory);
        Directory.CreateDirectory(cloneHeroSongDirectory);

        return new AppConfigRoot
        {
            Runtime = new RuntimeAppConfig
            {
                TempDirectory = tempDirectory,
                DownloadDirectory = downloadDirectory,
                StagingDirectory = stagingDirectory,
                OutputDirectory = outputDirectory,
                CloneHeroDataDirectory = cloneHeroDataDirectory,
                CloneHeroSongDirectory = cloneHeroSongDirectory,
                UseMockData = false,
            },
            GoogleAuth = new GoogleAuthConfig
            {
                AndroidClientId = "android-client",
                DesktopClientId = "desktop-client",
            },
        };
    }

    private static SettingsViewModel CreateSettingsViewModel(
        ISettingsOrchestrator orchestrator,
        ISecretStore secrets,
        ICloudStorageAccountService cloudAccount,
        bool? isAndroidPlatform = null)
    {
        ConstructorInfo? constructor = typeof(SettingsViewModel).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [
                typeof(ISettingsOrchestrator),
                typeof(ISecretStore),
                typeof(ICloudStorageAccountService),
                typeof(Action<Action>),
                typeof(bool?),
            ],
            modifiers: null);

        Assert.NotNull(constructor);

        return (SettingsViewModel)constructor.Invoke([
            orchestrator,
            secrets,
            cloudAccount,
            (Action<Action>)(action => action()),
            isAndroidPlatform,
        ]);
    }

    private sealed class FakeSettingsOrchestrator : ISettingsOrchestrator
    {
        public AppConfigRoot Current { get; private set; }

        public int UpdateCallCount { get; private set; }
        public int ReloadCallCount { get; private set; }

        public event Action<AppConfigRoot>? SettingsChanged;

        public FakeSettingsOrchestrator(AppConfigRoot current)
        {
            Current = current;
        }

        public Task<ConfigValidationResult> UpdateAsync(Action<AppConfigRoot> update, CancellationToken cancellationToken = default)
        {
            UpdateCallCount++;
            update(Current);
            SettingsChanged?.Invoke(Current);
            return Task.FromResult(ConfigValidationResult.Success);
        }

        public Task ReloadAsync(CancellationToken cancellationToken = default)
        {
            ReloadCallCount++;
            SettingsChanged?.Invoke(Current);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemorySecretStore : ISecretStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.TryGetValue(key, out string? value);
            return Task.FromResult<string?>(value);
        }

        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> ContainsAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_values.ContainsKey(key));
        }
    }

    private sealed class FakeCloudStorageAccountService : ICloudStorageAccountService
    {
        public bool TryRestoreSessionResult { get; set; }
        public Exception? TryRestoreSessionException { get; set; }

        public int TryRestoreSessionCallCount { get; private set; }
        public int LinkCallCount { get; private set; }
        public int UnlinkCallCount { get; private set; }

        public string ProviderId => "google-drive";
        public string ProviderDisplayName => "Google Drive";

        public Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default)
        {
            TryRestoreSessionCallCount++;
            if (TryRestoreSessionException is not null)
            {
                throw TryRestoreSessionException;
            }

            return Task.FromResult(TryRestoreSessionResult);
        }

        public Task LinkAsync(CancellationToken cancellationToken = default)
        {
            LinkCallCount++;
            return Task.CompletedTask;
        }

        public Task UnlinkAsync(CancellationToken cancellationToken = default)
        {
            UnlinkCallCount++;
            return Task.CompletedTask;
        }
    }
}
