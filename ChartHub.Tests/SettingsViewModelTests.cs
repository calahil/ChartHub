using System.Net;
using System.Reflection;

using ChartHub.Configuration.Interfaces;
using ChartHub.Configuration.Models;
using ChartHub.Configuration.Secrets;
using ChartHub.Services;
using ChartHub.Tests.TestInfrastructure;
using ChartHub.ViewModels;

using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;

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
        var googleAuthProvider = new FakeGoogleAuthProvider();
        var serverApiClient = new FakeChartHubServerApiClient();
        await secrets.SetAsync(SecretKeys.GoogleRefreshToken, "stored-refresh-token");

        using SettingsViewModel sut = CreateSettingsViewModel(orchestrator, secrets, googleAuthProvider, serverApiClient);
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
        var googleAuthProvider = new FakeGoogleAuthProvider();
        var serverApiClient = new FakeChartHubServerApiClient();

        using SettingsViewModel sut = CreateSettingsViewModel(orchestrator, secrets, googleAuthProvider, serverApiClient);
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
        var googleAuthProvider = new FakeGoogleAuthProvider();
        var serverApiClient = new FakeChartHubServerApiClient();

        using SettingsViewModel sut = CreateSettingsViewModel(orchestrator, secrets, googleAuthProvider, serverApiClient);
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
    public async Task AuthenticateServerCommand_WithoutBaseUrl_SetsError()
    {
        using var temp = new TemporaryDirectoryFixture("settings-vm-server-auth-missing-base-url");
        AppConfigRoot config = CreateConfig(temp.RootPath);
        config.Runtime.ServerApiBaseUrl = string.Empty;

        var orchestrator = new FakeSettingsOrchestrator(config);
        var secrets = new InMemorySecretStore();
        var googleAuthProvider = new FakeGoogleAuthProvider();
        var serverApiClient = new FakeChartHubServerApiClient();

        using SettingsViewModel sut = CreateSettingsViewModel(orchestrator, secrets, googleAuthProvider, serverApiClient);
        await Task.Yield();

        await sut.AuthenticateServerCommand.ExecuteAsync(null);

        Assert.True(sut.HasServerAuthenticationError);
        Assert.Equal("Set Runtime.ServerApiBaseUrl before authenticating.", sut.ServerAuthenticationErrorMessage);
        Assert.Equal("ChartHub Server authentication is not configured.", sut.ServerAuthenticationStatusMessage);
        Assert.Equal(0, googleAuthProvider.AuthorizeInteractiveCallCount);
    }

    [Fact]
    public async Task AuthenticateServerCommand_ExchangesGoogleTokenAndPersistsServerToken()
    {
        using var temp = new TemporaryDirectoryFixture("settings-vm-server-auth");
        var orchestrator = new FakeSettingsOrchestrator(CreateConfig(temp.RootPath));
        var secrets = new InMemorySecretStore();
        var googleAuthProvider = new FakeGoogleAuthProvider
        {
            InteractiveCredential = CreateGoogleCredential(idToken: "google-id-token"),
        };
        var serverApiClient = new FakeChartHubServerApiClient
        {
            ExchangeResponse = new ChartHubServerAuthExchangeResponse("server-access-token", DateTimeOffset.UtcNow.AddHours(1)),
        };

        using SettingsViewModel sut = CreateSettingsViewModel(orchestrator, secrets, googleAuthProvider, serverApiClient);
        await Task.Yield();

        await sut.AuthenticateServerCommand.ExecuteAsync(null);

        Assert.Equal(1, googleAuthProvider.AuthorizeInteractiveCallCount);
        Assert.Equal(1, serverApiClient.ExchangeCallCount);
        Assert.Equal("https://localhost:5001", serverApiClient.LastBaseUrl);
        Assert.Equal("google-id-token", serverApiClient.LastGoogleIdToken);
        Assert.Equal("server-access-token", orchestrator.Current.Runtime.ServerApiAuthToken);
        Assert.Equal("ChartHub Server authentication succeeded.", sut.ServerAuthenticationStatusMessage);
        Assert.False(sut.HasServerAuthenticationError);
        Assert.False(sut.IsServerAuthenticationBusy);
    }

    [Fact]
    public async Task AuthenticateServerCommand_Forbidden_ShowsAllowlistGuidance()
    {
        using var temp = new TemporaryDirectoryFixture("settings-vm-server-auth-forbidden-guidance");
        var orchestrator = new FakeSettingsOrchestrator(CreateConfig(temp.RootPath));
        var secrets = new InMemorySecretStore();
        var googleAuthProvider = new FakeGoogleAuthProvider
        {
            InteractiveCredential = CreateGoogleCredential(idToken: "google-id-token"),
        };
        var serverApiClient = new FakeChartHubServerApiClient
        {
            ExchangeHandler = (_, _) => throw new ChartHubServerApiException(HttpStatusCode.Forbidden, "Forbidden", string.Empty, null),
        };

        using SettingsViewModel sut = CreateSettingsViewModel(orchestrator, secrets, googleAuthProvider, serverApiClient);
        await Task.Yield();

        await sut.AuthenticateServerCommand.ExecuteAsync(null);

        Assert.True(sut.HasServerAuthenticationError);
        Assert.Equal("Your Google account is not allowlisted in ChartHub Server Auth:AllowedEmails.", sut.ServerAuthenticationErrorMessage);
        Assert.Equal("ChartHub Server authentication failed.", sut.ServerAuthenticationStatusMessage);
    }

    [Fact]
    public async Task AuthenticateServerCommand_DispatchesUiStateUpdatesThroughPostCallback()
    {
        using var temp = new TemporaryDirectoryFixture("settings-vm-server-auth-ui-dispatch");
        var orchestrator = new FakeSettingsOrchestrator(CreateConfig(temp.RootPath));
        var secrets = new InMemorySecretStore();
        var googleAuthProvider = new FakeGoogleAuthProvider
        {
            InteractiveCredential = CreateGoogleCredential(idToken: "google-id-token"),
        };
        var serverApiClient = new FakeChartHubServerApiClient
        {
            ExchangeResponse = new ChartHubServerAuthExchangeResponse("server-access-token", DateTimeOffset.UtcNow.AddHours(1)),
        };

        int dispatchCount = 0;
        Action<Action> postToUi = action =>
        {
            Interlocked.Increment(ref dispatchCount);
            action();
        };

        using SettingsViewModel sut = CreateSettingsViewModel(orchestrator, secrets, googleAuthProvider, serverApiClient, postToUi: postToUi);
        await Task.Yield();

        dispatchCount = 0;
        await sut.AuthenticateServerCommand.ExecuteAsync(null);

        Assert.True(dispatchCount >= 3);
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
        IGoogleAuthProvider googleAuthProvider,
        IChartHubServerApiClient serverApiClient,
        Action<Action>? postToUi = null,
        bool? isAndroidPlatform = null)
    {
        ConstructorInfo? constructor = typeof(SettingsViewModel).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [
                typeof(ISettingsOrchestrator),
                typeof(ISecretStore),
                typeof(IGoogleAuthProvider),
                typeof(IChartHubServerApiClient),
                typeof(Action<Action>),
                typeof(bool?),
            ],
            modifiers: null);

        Assert.NotNull(constructor);

        return (SettingsViewModel)constructor.Invoke([
            orchestrator,
            secrets,
            googleAuthProvider,
            serverApiClient,
            postToUi ?? (Action<Action>)(action => action()),
            isAndroidPlatform,
        ]);
    }

    private static UserCredential CreateGoogleCredential(string idToken)
    {
        var initializer = new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = "test-client-id",
                ClientSecret = "test-client-secret",
            },
            Scopes = ["openid", "email", "profile"],
        };

        var flow = new GoogleAuthorizationCodeFlow(initializer);
        var token = new TokenResponse
        {
            IdToken = idToken,
            AccessToken = "test-access-token",
            RefreshToken = "test-refresh-token",
            IssuedUtc = DateTime.UtcNow,
            ExpiresInSeconds = 3600,
            TokenType = "Bearer",
        };

        return new UserCredential(flow, "user", token);
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

    private sealed class FakeGoogleAuthProvider : IGoogleAuthProvider
    {
        public UserCredential? InteractiveCredential { get; set; }
        public Exception? InteractiveException { get; set; }
        public int AuthorizeInteractiveCallCount { get; private set; }
        public int SignOutCallCount { get; private set; }

        public Task<UserCredential?> TryAuthorizeSilentAsync(IEnumerable<string> scopes, CancellationToken cancellationToken = default)
            => Task.FromResult<UserCredential?>(null);

        public Task SignOutAsync(UserCredential? credential, CancellationToken cancellationToken = default)
        {
            SignOutCallCount++;
            return Task.CompletedTask;
        }

        public Task<UserCredential> AuthorizeInteractiveAsync(IEnumerable<string> scopes, CancellationToken cancellationToken = default)
        {
            AuthorizeInteractiveCallCount++;
            if (InteractiveException is not null)
            {
                throw InteractiveException;
            }

            if (InteractiveCredential is null)
            {
                throw new InvalidOperationException("Interactive credential not configured for test.");
            }

            return Task.FromResult(InteractiveCredential);
        }
    }

    private sealed class FakeChartHubServerApiClient : IChartHubServerApiClient
    {
        public int ExchangeCallCount { get; private set; }
        public string LastBaseUrl { get; private set; } = string.Empty;
        public string LastGoogleIdToken { get; private set; } = string.Empty;
        public ChartHubServerAuthExchangeResponse ExchangeResponse { get; set; } =
            new("test-token", DateTimeOffset.UtcNow.AddMinutes(30));
        public Func<string, string, ChartHubServerAuthExchangeResponse>? ExchangeHandler { get; set; }

        public Task<ChartHubServerAuthExchangeResponse> ExchangeGoogleTokenAsync(
            string baseUrl,
            string googleIdToken,
            CancellationToken cancellationToken = default)
        {
            ExchangeCallCount++;
            LastBaseUrl = baseUrl;
            LastGoogleIdToken = googleIdToken;
            if (ExchangeHandler is not null)
            {
                return Task.FromResult(ExchangeHandler(baseUrl, googleIdToken));
            }

            return Task.FromResult(ExchangeResponse);
        }

        public Task<ChartHubServerDownloadJobResponse> CreateDownloadJobAsync(
            string baseUrl,
            string bearerToken,
            ChartHubServerCreateDownloadJobRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<ChartHubServerDownloadJobResponse>> ListDownloadJobsAsync(
            string baseUrl,
            string bearerToken,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public async IAsyncEnumerable<IReadOnlyList<ChartHubServerDownloadJobResponse>> StreamDownloadJobsAsync(
            string baseUrl,
            string bearerToken,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            yield break;
        }

        public Task RequestCancelDownloadJobAsync(
            string baseUrl,
            string bearerToken,
            Guid jobId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<ChartHubServerJobLogEntry>> GetJobLogsAsync(string baseUrl, string bearerToken, Guid jobId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ChartHubServerJobLogEntry>>([]);

        public Task<ChartHubServerDownloadJobResponse> RequestInstallDownloadJobAsync(
            string baseUrl,
            string bearerToken,
            Guid jobId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
