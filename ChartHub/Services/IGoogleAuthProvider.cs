using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Configuration;
using ChartHub.Configuration.Interfaces;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;  // used for JsonDocument only
#if ANDROID
using System.Net.Http;
#endif

namespace ChartHub.Services;

public interface IGoogleAuthProvider
{
    Task<UserCredential> AuthorizeInteractiveAsync(IEnumerable<string> scopes, CancellationToken cancellationToken = default);
    Task<UserCredential?> TryAuthorizeSilentAsync(IEnumerable<string> scopes, CancellationToken cancellationToken = default);
    Task SignOutAsync(UserCredential? credential, CancellationToken cancellationToken = default);
}

public sealed class DesktopGoogleAuthProvider(IConfiguration configuration, ISecretStore secretStore) : IGoogleAuthProvider
{
    private readonly IConfiguration _configuration = configuration;
    private readonly ISecretStore _secretStore = secretStore;
    private readonly string _legacyCredentialStorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Personal),
        ".credentials/drive-dotnet-maui.json");
    private const string DataStorePrefix = "google-oauth-desktop";

    public async Task<UserCredential> AuthorizeInteractiveAsync(IEnumerable<string> scopes, CancellationToken cancellationToken = default)
    {
        var initializer = BuildFlowInitializer(scopes);
        return await GoogleWebAuthorizationBroker.AuthorizeAsync(
            initializer,
            scopes,
            "user",
            cancellationToken,
            CreateTokenDataStore());
    }

    public async Task<UserCredential?> TryAuthorizeSilentAsync(IEnumerable<string> scopes, CancellationToken cancellationToken = default)
    {
        try
        {
            return await LoadCachedCredentialAsync(scopes, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task SignOutAsync(UserCredential? credential, CancellationToken cancellationToken = default)
    {
        if (credential is not null)
        {
            try
            {
                await credential.RevokeTokenAsync(cancellationToken);
            }
            catch
            {
                // Ignore revoke failures; cache cleanup below still signs the user out locally.
            }
        }

        await CreateTokenDataStore().ClearAsync().ConfigureAwait(false);
        DeleteLegacyCredentialStore();
    }

    private GoogleAuthorizationCodeFlow.Initializer BuildFlowInitializer(IEnumerable<string> scopes)
    {
        var clientId = ResolveDesktopClientId();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException(
                "Desktop Google OAuth client id is missing. Configure GoogleDrive:desktop_client_id " +
                "(or GoogleDrive:client_id), or set GOOGLEDRIVE_DESKTOP_CLIENT_ID / GOOGLEDRIVE_CLIENT_ID.");
        }

        return new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = clientId,
                ClientSecret = ResolveDesktopClientSecret(),
            },
            Scopes = scopes,
        };
    }

    private IDataStore CreateTokenDataStore()
    {
        return new GoogleAuthTokenDataStore(_secretStore, DataStorePrefix);
    }

    private async Task<UserCredential?> LoadCachedCredentialAsync(
        IEnumerable<string> scopes,
        CancellationToken cancellationToken)
    {
        var initializer = BuildFlowInitializer(scopes);
        initializer.DataStore = CreateTokenDataStore();
        var flow = new GoogleAuthorizationCodeFlow(initializer);

        var token = await flow.LoadTokenAsync("user", cancellationToken).ConfigureAwait(false);
        if (token is null)
            token = await LoadLegacyTokenAsync(flow, cancellationToken).ConfigureAwait(false);

        if (token is null)
            return null;

        var credential = new UserCredential(flow, "user", token);
        if (token.IsStale)
            await credential.RefreshTokenAsync(cancellationToken).ConfigureAwait(false);

        return credential;
    }

    private async Task<TokenResponse?> LoadLegacyTokenAsync(
        GoogleAuthorizationCodeFlow currentFlow,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_legacyCredentialStorePath) && !File.Exists(_legacyCredentialStorePath))
            return null;

        var legacyInitializer = BuildFlowInitializer(currentFlow.Scopes);
        legacyInitializer.DataStore = new FileDataStore(_legacyCredentialStorePath, true);
        var legacyFlow = new GoogleAuthorizationCodeFlow(legacyInitializer);
        var legacyToken = await legacyFlow.LoadTokenAsync("user", cancellationToken).ConfigureAwait(false);
        if (legacyToken is null)
            return null;

        await currentFlow.DataStore.StoreAsync("user", legacyToken).ConfigureAwait(false);
        DeleteLegacyCredentialStore();
        return legacyToken;
    }

    private void DeleteLegacyCredentialStore()
    {
        if (Directory.Exists(_legacyCredentialStorePath))
        {
            Directory.Delete(_legacyCredentialStorePath, true);
            return;
        }

        if (File.Exists(_legacyCredentialStorePath))
            File.Delete(_legacyCredentialStorePath);
    }

    private string? ResolveDesktopClientId()
    {
        return _configuration["GoogleDrive:desktop_client_id"]
            ?? _configuration["GoogleDrive:DesktopClientId"]
            ?? Environment.GetEnvironmentVariable("GOOGLEDRIVE_DESKTOP_CLIENT_ID")
            ?? _configuration["GoogleDrive:client_id"]
            ?? _configuration["GoogleDrive:ClientId"]
            ?? Environment.GetEnvironmentVariable("GOOGLEDRIVE_CLIENT_ID");
    }

    private string? ResolveDesktopClientSecret()
    {
        return _configuration["GoogleDrive:desktop_client_secret"]
            ?? _configuration["GoogleDrive:DesktopClientSecret"]
            ?? Environment.GetEnvironmentVariable("GOOGLEDRIVE_DESKTOP_CLIENT_SECRET")
            ?? _configuration["GoogleDrive:client_secret"]
            ?? _configuration["GoogleDrive:ClientSecret"]
            ?? Environment.GetEnvironmentVariable("GOOGLEDRIVE_CLIENT_SECRET");
    }
}

/// <summary>
/// Android Google auth provider using native Google Sign-In (Android OAuth client)
/// without browser custom redirect URI handling.
/// </summary>
public sealed class AndroidGoogleAuthProvider(IConfiguration configuration, ISecretStore secretStore) : IGoogleAuthProvider
{
    private readonly IConfiguration _configuration = configuration;
    private readonly ISecretStore _secretStore = secretStore;
    private static readonly Uri DefaultAuthUri = new("https://accounts.google.com/o/oauth2/v2/auth");
    private static readonly Uri DefaultTokenUri = new("https://oauth2.googleapis.com/token");
    private const string DataStorePrefix = "google-oauth-android";

    internal const string FallbackAndroidClientId = "32662681450-gk9vocigkqomedf3vkk1fjtu20slobo1.apps.googleusercontent.com";

    private static string GetCredentialStorePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "google-auth-android");

    // Android app client id (package + SHA-bound). Kept separate from exchange client id.
    private string? ResolveAndroidClientId() =>
        _configuration["GoogleDrive:android_client_id"]
        ?? _configuration["GoogleDrive:AndroidClientId"]
        ?? Environment.GetEnvironmentVariable("GOOGLEDRIVE_ANDROID_CLIENT_ID")
        ?? FallbackAndroidClientId;

    private string ResolveAndroidRedirectUri(string clientId)
    {
        var suffix = ".apps.googleusercontent.com";
        if (!clientId.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Android OAuth client ID format is invalid.");

        var prefix = clientId[..^suffix.Length];
        return $"com.googleusercontent.apps.{prefix}:/oauth2redirect";
    }

    private Uri ResolveAuthUri() =>
        new(_configuration["GoogleDrive:auth_uri"] ?? _configuration["GoogleDrive:AuthUri"] ?? DefaultAuthUri.ToString());

    private Uri ResolveTokenUri() =>
        new(_configuration["GoogleDrive:token_uri"] ?? _configuration["GoogleDrive:TokenUri"] ?? DefaultTokenUri.ToString());

    private GoogleAuthorizationCodeFlow.Initializer BuildFlowInitializer(IEnumerable<string> scopes) =>
        new()
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = ResolveAndroidClientId()!,
            },
            Scopes = scopes,
        };

    public async Task<UserCredential> AuthorizeInteractiveAsync(
        IEnumerable<string> scopes,
        CancellationToken cancellationToken = default)
    {
        var androidClientId = ResolveAndroidClientId();
        if (string.IsNullOrWhiteSpace(androidClientId))
            throw new InvalidOperationException(
                "Android Google OAuth client ID not configured. " +
                "Add 'GoogleDrive:android_client_id' to appsettings/user-secrets, " +
                "or set GOOGLEDRIVE_ANDROID_CLIENT_ID in the environment.");

#if ANDROID
        var redirectUri = ResolveAndroidRedirectUri(androidClientId);
        var (codeVerifier, codeChallenge) = CreatePkcePair();
        var state = Guid.NewGuid().ToString("N");
        var authorizationUri = BuildAuthorizationUri(androidClientId, redirectUri, scopes, codeChallenge, state);

        var waitForCode = AndroidOAuthRedirectBridge.WaitForCodeAsync(cancellationToken);
        AndroidOAuthRedirectBridge.LaunchAuthorizationUri(authorizationUri);

        var authCode = await waitForCode;
        var token = await ExchangeAuthCodeWithPkceAsync(
            ResolveTokenUri(),
            androidClientId,
            redirectUri,
            authCode,
            codeVerifier,
            cancellationToken);

        return await CreateCredentialFromTokenResponseAsync(token, scopes).ConfigureAwait(false);
#else
        throw new PlatformNotSupportedException(
            "AndroidGoogleAuthProvider is only supported on the Android target.");
#endif
    }

    public async Task<UserCredential?> TryAuthorizeSilentAsync(
        IEnumerable<string> scopes,
        CancellationToken cancellationToken = default)
    {
#if ANDROID
        try
        {
            if (string.IsNullOrWhiteSpace(ResolveAndroidClientId()))
                return null;

            var token = await LoadCachedTokenAsync(scopes, cancellationToken);
            if (token is null)
                return null;

            return await CreateCredentialFromTokenResponseAsync(token, scopes).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
#else
        return await Task.FromResult<UserCredential?>(null);
#endif
    }

    public async Task SignOutAsync(UserCredential? credential, CancellationToken cancellationToken = default)
    {
        if (credential is not null)
        {
            try { await credential.RevokeTokenAsync(cancellationToken); }
            catch { /* best-effort revoke; local cache cleanup follows */ }
        }

        var path = GetCredentialStorePath();
        await CreateTokenDataStore().ClearAsync().ConfigureAwait(false);
        DeleteLegacyCredentialStore(path);

#if ANDROID
        await Task.CompletedTask;
#endif
    }

    private async Task<UserCredential> CreateCredentialFromTokenResponseAsync(TokenResponse token, IEnumerable<string> scopes)
    {
        var initializer = BuildFlowInitializer(scopes);
        initializer.DataStore = CreateTokenDataStore();
        var flow = new GoogleAuthorizationCodeFlow(initializer);
        await flow.DataStore.StoreAsync("user", token).ConfigureAwait(false);

        return new UserCredential(flow, "user", token);
    }

    private IDataStore CreateTokenDataStore() => new GoogleAuthTokenDataStore(_secretStore, DataStorePrefix);

    private string BuildAuthorizationUri(
        string clientId,
        string redirectUri,
        IEnumerable<string> scopes,
        string codeChallenge,
        string state)
    {
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', scopes),
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            ["access_type"] = "offline",
            ["prompt"] = "consent",
        };

        var query = string.Join("&", parameters.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        return $"{ResolveAuthUri()}?{query}";
    }

    private static (string CodeVerifier, string CodeChallenge) CreatePkcePair()
    {
        var random = RandomNumberGenerator.GetBytes(64);
        var verifier = Base64UrlEncode(random);
        var challengeBytes = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        var challenge = Base64UrlEncode(challengeBytes);
        return (verifier, challenge);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static async Task<TokenResponse> ExchangeAuthCodeWithPkceAsync(
        Uri tokenUri,
        string clientId,
        string redirectUri,
        string authCode,
        string codeVerifier,
        CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["grant_type"] = "authorization_code",
            ["code"] = authCode,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier,
        });

        using var response = await httpClient.PostAsync(tokenUri, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Google token exchange failed ({(int)response.StatusCode}): {body}");

        // TokenResponse uses Newtonsoft.Json [JsonProperty] attributes, so System.Text.Json
        // cannot deserialize it — map fields manually from the raw JSON document instead.
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        return new TokenResponse
        {
            AccessToken = root.TryGetProperty("access_token", out var at) ? at.GetString() : null,
            RefreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
            TokenType = root.TryGetProperty("token_type", out var tt) ? tt.GetString() : "Bearer",
            IdToken = root.TryGetProperty("id_token", out var idt) ? idt.GetString() : null,
            ExpiresInSeconds = root.TryGetProperty("expires_in", out var ei) && ei.TryGetInt64(out var eiVal) ? eiVal : null,
            Scope = root.TryGetProperty("scope", out var sc) ? sc.GetString() : null,
            // IssuedUtc must be set; leaving it at default (DateTime.MinValue) makes the
            // credential appear permanently stale, causing immediate refresh failures.
            IssuedUtc = DateTime.UtcNow,
        };
    }

    private async Task<TokenResponse?> LoadCachedTokenAsync(IEnumerable<string> scopes, CancellationToken cancellationToken)
    {
        var initializer = BuildFlowInitializer(scopes);
        initializer.DataStore = CreateTokenDataStore();
        var flow = new GoogleAuthorizationCodeFlow(initializer);
        var token = await flow.LoadTokenAsync("user", cancellationToken);
        if (token is null)
            token = await LoadLegacyTokenAsync(flow, cancellationToken).ConfigureAwait(false);

        if (token is null)
            return null;

        var credential = new UserCredential(flow, "user", token);
        if (token.IsStale)
            await credential.RefreshTokenAsync(cancellationToken);

        return credential.Token;
    }

    private async Task<TokenResponse?> LoadLegacyTokenAsync(
        GoogleAuthorizationCodeFlow currentFlow,
        CancellationToken cancellationToken)
    {
        var legacyPath = GetCredentialStorePath();
        if (!Directory.Exists(legacyPath) && !File.Exists(legacyPath))
            return null;

        var legacyInitializer = BuildFlowInitializer(currentFlow.Scopes);
        legacyInitializer.DataStore = new FileDataStore(legacyPath, fullPath: true);
        var legacyFlow = new GoogleAuthorizationCodeFlow(legacyInitializer);
        var legacyToken = await legacyFlow.LoadTokenAsync("user", cancellationToken).ConfigureAwait(false);
        if (legacyToken is null)
            return null;

        await currentFlow.DataStore.StoreAsync("user", legacyToken).ConfigureAwait(false);
        DeleteLegacyCredentialStore(legacyPath);
        return legacyToken;
    }

    private static void DeleteLegacyCredentialStore(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        else if (File.Exists(path))
            File.Delete(path);
    }

#if ANDROID
    // Android native sign-in interaction is implemented in Android/GoogleSignInActivity.cs
#endif
}
