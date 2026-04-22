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
        await secrets.SetAsync(SecretKeys.GoogleRefreshToken, "stored-refresh-token");

        using SettingsViewModel sut = CreateSettingsViewModel(orchestrator, secrets);
        await Task.Yield();

        Assert.Equal(10, sut.Fields.Count);
        Assert.Contains(sut.Fields, field => field.Key == "Runtime.RhythmVerseSource" && field.IsDropdownEditor);
        Assert.Contains(sut.Fields, field => field.Key == "Runtime.UseMockData" && field.IsToggleEditor);
        Assert.Contains(sut.Fields, field => field.Key == "Runtime.ServerApiAuthToken" && field.IsTextEditor);
        Assert.Contains(sut.Fields, field => field.Key == "Runtime.ServerApiBaseUrl" && field.IsTextEditor);
        Assert.Contains(sut.Fields, field => field.Key == "Runtime.UiCulture" && field.IsTextEditor);
        Assert.Contains(sut.Fields, field => field.Key == "Runtime.AndroidVolumeButtonsControlServerVolume" && field.IsToggleEditor);
        Assert.Contains(sut.Fields, field => field.Key == "Runtime.MouseSpeedMultiplier" && field.IsNumberEditor);
        Assert.Contains(sut.Fields, field => field.Key == "GoogleAuth.AndroidClientId" && field.IsTextEditor);

        int expectedSecretCount = sut.IsDeveloperBuild ? 3 : 0;
        Assert.Equal(expectedSecretCount, sut.Secrets.Count);
    }

    [Fact]
    public async Task SaveCommand_WithValidChanges_PersistsAndSetsSavedStatus()
    {
        using var temp = new TemporaryDirectoryFixture("settings-vm-save");
        var orchestrator = new FakeSettingsOrchestrator(CreateConfig(temp.RootPath));
        var secrets = new InMemorySecretStore();

        using SettingsViewModel sut = CreateSettingsViewModel(orchestrator, secrets);
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
    public async Task SecretCommands_SaveAndClear_UpdateStateAndStore()
    {
        using var temp = new TemporaryDirectoryFixture("settings-vm-secrets");
        var orchestrator = new FakeSettingsOrchestrator(CreateConfig(temp.RootPath));
        var secrets = new InMemorySecretStore();

        using SettingsViewModel sut = CreateSettingsViewModel(orchestrator, secrets);
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

    private static AppConfigRoot CreateConfig(string rootPath)
    {
        _ = rootPath;

        return new AppConfigRoot
        {
            Runtime = new RuntimeAppConfig
            {
                ServerApiBaseUrl = "https://localhost:5001",
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
        Action<Action>? postToUi = null,
        bool? isAndroidPlatform = null)
    {
        ConstructorInfo? constructor = typeof(SettingsViewModel).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [
                typeof(ISettingsOrchestrator),
                typeof(ISecretStore),
                typeof(Action<Action>),
                typeof(bool?),
            ],
            modifiers: null);

        Assert.NotNull(constructor);

        return (SettingsViewModel)constructor.Invoke([
            orchestrator,
            secrets,
            postToUi ?? (Action<Action>)(action => action()),
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
}
