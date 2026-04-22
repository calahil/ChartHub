using System.ComponentModel;
using System.Reflection;

using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using ChartHub.Configuration.Secrets;
using ChartHub.Services;
using ChartHub.Tests.TestInfrastructure;
using ChartHub.Utilities;
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
        await secrets.SetAsync(SecretKeys.GoogleDesktopClientSecret, "stored-desktop-secret");

        using SettingsViewModel sut = CreateSettingsViewModel(orchestrator, secrets);
        await Task.Yield();

        // Hidden fields (ServerApiAuthToken, ServerApiBaseUrl, InstallLogExpanded, LastSelectedMainTabIndex) must NOT appear.
        // Developer-only fields (UseMockData, Google OAuth IDs) only appear when ShowDeveloperSettings is false -> excluded.
        // Non-developer, non-hidden, non-platform-restricted fields on desktop: UiCulture, MouseSpeedMultiplier, DeviceDisplayNameOverride.
        Assert.Contains(sut.Fields, field => field.Key == "Runtime.RhythmVerseSource" && field.IsDropdownEditor);
        Assert.Contains(sut.Fields, field => field.Key == "Runtime.UiCulture" && field.IsTextEditor);
        Assert.Contains(sut.Fields, field => field.Key == "Runtime.MouseSpeedMultiplier" && field.IsNumberEditor);
        Assert.Contains(sut.Fields, field => field.Key == "Runtime.DeviceDisplayNameOverride" && field.IsTextEditor);

        // Developer fields should be absent when ShowDeveloperSettings=false
        Assert.DoesNotContain(sut.Fields, f => f.Key == "Runtime.UseMockData");
        Assert.DoesNotContain(sut.Fields, f => f.Key == "GoogleAuth.DesktopClientId");

        int expectedSecretCount = sut.IsDeveloperBuild ? 1 : 0;
        Assert.Equal(expectedSecretCount, sut.Secrets.Count);
    }

    [Fact]
    public async Task Constructor_WithDeveloperSettingsOn_IncludesDeveloperOnlyFields()
    {
        using var temp = new TemporaryDirectoryFixture("settings-vm-dev");
        var orchestrator = new FakeSettingsOrchestrator(CreateConfig(temp.RootPath));
        var secrets = new InMemorySecretStore();

        using SettingsViewModel sut = CreateSettingsViewModel(orchestrator, secrets);
        await Task.Yield();

        sut.ShowDeveloperSettings = true;

        Assert.Contains(sut.Fields, f => f.Key == "Runtime.UseMockData");
        // Desktop platform: DesktopClientId visible; AndroidClientId excluded by platform filter
        Assert.Contains(sut.Fields, f => f.Key == "GoogleAuth.DesktopClientId");
        Assert.DoesNotContain(sut.Fields, f => f.Key == "GoogleAuth.AndroidClientId");
    }

    [Fact]
    public async Task FieldChange_PersistsImmediately_WithoutExplicitSave()
    {
        using var temp = new TemporaryDirectoryFixture("settings-vm-immediate");
        var orchestrator = new FakeSettingsOrchestrator(CreateConfig(temp.RootPath));
        var secrets = new InMemorySecretStore();

        using SettingsViewModel sut = CreateSettingsViewModel(orchestrator, secrets);
        await Task.Yield();

        SettingsFieldViewModel mouseField = Assert.Single(sut.Fields, item => item.Key == "Runtime.MouseSpeedMultiplier");
        mouseField.NumberValue = 8.0;

        // Allow the async fire-and-forget persist task to run
        await Task.Delay(100);

        Assert.Equal(8.0, orchestrator.Current.Runtime.MouseSpeedMultiplier, precision: 5);
        Assert.True(orchestrator.UpdateCallCount > 0);
    }

    [Fact]
    public async Task ServerApiBaseUrl_SetDirectly_PersistsViaGlobalSettings()
    {
        using var temp = new TemporaryDirectoryFixture("settings-vm-url");
        var orchestrator = new FakeSettingsOrchestrator(CreateConfig(temp.RootPath));
        var secrets = new InMemorySecretStore();
        var globalSettings = new AppGlobalSettings(orchestrator);

        using SettingsViewModel sut = CreateSettingsViewModel(orchestrator, secrets, globalSettings: globalSettings);
        await Task.Yield();

        sut.ServerApiBaseUrl = "  https://new-server:5002  ";

        await Task.Delay(50);

        Assert.Equal("https://new-server:5002", sut.ServerApiBaseUrl);
        Assert.Equal("https://new-server:5002", globalSettings.ServerApiBaseUrl);
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

    [Fact]
    public async Task AuthStatusText_ReflectsSessionState()
    {
        using var temp = new TemporaryDirectoryFixture("settings-vm-auth");
        var orchestrator = new FakeSettingsOrchestrator(CreateConfig(temp.RootPath));
        var secrets = new InMemorySecretStore();
        var fakeAuth = new FakeAuthSessionService();

        using SettingsViewModel sut = CreateSettingsViewModel(orchestrator, secrets, authSession: fakeAuth);
        await Task.Yield();

        Assert.Equal("Checking saved session...", sut.AuthStatusText);

        fakeAuth.SetState(AuthSessionState.Unauthenticated, null);
        Assert.Equal("Signed out", sut.AuthStatusText);

        fakeAuth.SetState(AuthSessionState.Authenticated, "user@example.com");
        Assert.Equal("Signed in as user@example.com", sut.AuthStatusText);
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
        bool? isAndroidPlatform = null,
        AppGlobalSettings? globalSettings = null,
        FakeAuthSessionService? authSession = null)
    {
        AppGlobalSettings gs = globalSettings ?? new AppGlobalSettings(orchestrator);
        FakeAuthSessionService auth = authSession ?? new FakeAuthSessionService();
        var fakeApiClient = new FakeChartHubServerApiClient();

        ConstructorInfo? constructor = typeof(SettingsViewModel).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [
                typeof(ISettingsOrchestrator),
                typeof(ISecretStore),
                typeof(AppGlobalSettings),
                typeof(IAuthSessionService),
                typeof(IChartHubServerApiClient),
                typeof(Action<Action>),
                typeof(bool?),
            ],
            modifiers: null);

        Assert.NotNull(constructor);

        return (SettingsViewModel)constructor.Invoke([
            orchestrator,
            secrets,
            gs,
            auth,
            fakeApiClient,
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

    private sealed class FakeAuthSessionService : IAuthSessionService
    {
        private AuthSessionState _state = AuthSessionState.Unknown;
        private string? _email;

        public AuthSessionState CurrentState => _state;
        public string? SignedInEmail => _email;
        public string? CurrentAccessToken => null;

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? SessionStateChanged;

        public void SetState(AuthSessionState state, string? email)
        {
            _state = state;
            _email = email;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentState)));
            SessionStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public Task AttemptSilentRestoreAsync() => Task.CompletedTask;
        public Task SignInAsync() => Task.CompletedTask;
        public Task SignOutAsync() => Task.CompletedTask;
        public bool IsTokenValidLocally() => _state == AuthSessionState.Authenticated;
        public Task AttemptSilentRefreshAsync() => Task.CompletedTask;
    }

    private sealed class FakeChartHubServerApiClient : IChartHubServerApiClient
    {
        public Task<string> GetHealthAsync(string baseUrl, CancellationToken cancellationToken = default)
            => Task.FromResult("ok");

        public Task<ChartHubServerAuthExchangeResponse> ExchangeGoogleTokenAsync(string baseUrl, string googleIdToken, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChartHubServerAuthExchangeResponse("fake-token", DateTimeOffset.UtcNow.AddHours(1)));

        public Task<ChartHubServerDownloadJobResponse> CreateDownloadJobAsync(string baseUrl, string bearerToken, ChartHubServerCreateDownloadJobRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ChartHubServerDownloadJobResponse>> ListDownloadJobsAsync(string baseUrl, string bearerToken, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ChartHubServerDownloadJobResponse>>([]);

        public IAsyncEnumerable<IReadOnlyList<ChartHubServerDownloadJobResponse>> StreamDownloadJobsAsync(string baseUrl, string bearerToken, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<IReadOnlyList<ChartHubServerDownloadJobResponse>>();

        public Task<IReadOnlyList<ChartHubServerJobLogEntry>> GetJobLogsAsync(string baseUrl, string bearerToken, Guid jobId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ChartHubServerJobLogEntry>>([]);

        public Task<ChartHubServerDownloadJobResponse> RequestInstallDownloadJobAsync(string baseUrl, string bearerToken, Guid jobId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task RequestCancelDownloadJobAsync(string baseUrl, string bearerToken, Guid jobId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
