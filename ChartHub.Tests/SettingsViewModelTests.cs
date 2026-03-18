using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using ChartHub.Configuration.Secrets;
using ChartHub.Services;
using ChartHub.ViewModels;
using ChartHub.Tests.TestInfrastructure;

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

        using var sut = new SettingsViewModel(orchestrator, secrets, cloudAccount);
        await Task.Yield();

        Assert.Equal(9, sut.Fields.Count);
        Assert.Contains(sut.Fields, field => field.Key == "Runtime.UseMockData" && field.IsToggleEditor);
        Assert.Contains(sut.Fields, field => field.Key == "Runtime.DownloadDirectory" && field.IsDirectoryPicker);
        Assert.Contains(sut.Fields, field => field.Key == "GoogleAuth.AndroidClientId" && field.IsTextEditor);
        Assert.Contains(sut.Fields, field => field.IsGroupHeaderVisible);
        Assert.Equal(3, sut.Secrets.Count);
        Assert.Contains(sut.Secrets, secret => secret.Key == SecretKeys.GoogleRefreshToken && secret.HasStoredValue);
    }

    [Fact]
    public async Task DirectoryField_InvalidPath_SetsValidationErrorAndDisablesSave()
    {
        using var temp = new TemporaryDirectoryFixture("settings-vm-validation");
        var orchestrator = new FakeSettingsOrchestrator(CreateConfig(temp.RootPath));
        var secrets = new InMemorySecretStore();
        var cloudAccount = new FakeCloudStorageAccountService();

        using var sut = new SettingsViewModel(orchestrator, secrets, cloudAccount);
        await Task.Yield();

        var field = Assert.Single(sut.Fields, item => item.Key == "Runtime.DownloadDirectory");
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

        using var sut = new SettingsViewModel(orchestrator, secrets, cloudAccount);
        await Task.Yield();

        var androidClientField = Assert.Single(sut.Fields, item => item.Key == "GoogleAuth.AndroidClientId");
        androidClientField.StringValue = "android-client-updated";

        Assert.True(sut.SaveCommand.CanExecute(null));
        await sut.SaveCommand.ExecuteAsync(null);

        Assert.Equal(1, orchestrator.UpdateCallCount);
        Assert.Equal("android-client-updated", orchestrator.Current.GoogleAuth.AndroidClientId);
        Assert.Equal("Settings saved.", sut.StatusMessage);
    }

    [Fact]
    public async Task SecretCommands_SaveAndClear_UpdateStateAndStore()
    {
        using var temp = new TemporaryDirectoryFixture("settings-vm-secrets");
        var orchestrator = new FakeSettingsOrchestrator(CreateConfig(temp.RootPath));
        var secrets = new InMemorySecretStore();
        var cloudAccount = new FakeCloudStorageAccountService();

        using var sut = new SettingsViewModel(orchestrator, secrets, cloudAccount);
        await Task.Yield();

        var secret = Assert.Single(sut.Secrets, item => item.Key == SecretKeys.GoogleDesktopClientSecret);
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

        using var sut = new SettingsViewModel(orchestrator, secrets, cloudAccount);
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

        using var sut = new SettingsViewModel(orchestrator, secrets, cloudAccount);
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

        using var sut = new SettingsViewModel(orchestrator, secrets, cloudAccount);
        await Task.Yield();

        Assert.True(sut.IsCloudAccountLinked);
        Assert.Equal("Google Drive linked.", sut.CloudAccountStatusMessage);
        Assert.False(sut.HasCloudAccountError);
    }

    private static AppConfigRoot CreateConfig(string rootPath)
    {
        var tempDirectory = Path.Combine(rootPath, "Temp");
        var downloadDirectory = Path.Combine(rootPath, "Downloads");
        var stagingDirectory = Path.Combine(rootPath, "Staging");
        var outputDirectory = Path.Combine(rootPath, "Output");
        var cloneHeroDataDirectory = Path.Combine(rootPath, "CloneHero");
        var cloneHeroSongDirectory = Path.Combine(cloneHeroDataDirectory, "Songs");

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

    private sealed class FakeSettingsOrchestrator : ISettingsOrchestrator
    {
        public AppConfigRoot Current { get; private set; }

        public int UpdateCallCount { get; private set; }

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
            SettingsChanged?.Invoke(Current);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemorySecretStore : ISecretStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.TryGetValue(key, out var value);
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
                throw TryRestoreSessionException;

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
