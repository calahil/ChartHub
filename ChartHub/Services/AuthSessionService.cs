using System.ComponentModel;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

using Avalonia.Threading;

using ChartHub.Utilities;

using Google.Apis.Auth.OAuth2;

namespace ChartHub.Services;

/// <summary>
/// Production session service for ChartHub Server authentication.
/// Manages Google → Server JWT exchange, token expiry validation, and reactive state updates.
/// </summary>
public sealed class AuthSessionService : IAuthSessionService, IDisposable
{
    private readonly IGoogleAuthProvider _googleAuthProvider;
    private readonly IChartHubServerApiClient _serverApiClient;
    private readonly AppGlobalSettings _globalSettings;
    private readonly Func<Action, Task> _uiInvoke;
    private static readonly string[] AuthScopes = ["openid", "email", "profile"];
    private readonly object _stateLock = new();

    private AuthSessionState _currentState = AuthSessionState.Unknown;
    private string? _signedInEmail;
    private string? _currentAccessToken;
    private DateTime _tokenExpiryUtc = DateTime.MinValue;
    private bool _disposed;

    public AuthSessionState CurrentState
    {
        get
        {
            lock (_stateLock)
            {
                return _currentState;
            }
        }
        private set
        {
            lock (_stateLock)
            {
                if (_currentState == value)
                {
                    return;
                }

                _currentState = value;
            }

            OnPropertyChanged();
            SessionStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string? SignedInEmail
    {
        get
        {
            lock (_stateLock)
            {
                return _signedInEmail;
            }
        }
        private set
        {
            lock (_stateLock)
            {
                _signedInEmail = value;
            }

            OnPropertyChanged();
        }
    }

    public string? CurrentAccessToken
    {
        get
        {
            lock (_stateLock)
            {
                return _currentAccessToken;
            }
        }
        private set
        {
            lock (_stateLock)
            {
                _currentAccessToken = value;
            }

            OnPropertyChanged();
        }
    }

    public event EventHandler? SessionStateChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public AuthSessionService(
        IGoogleAuthProvider googleAuthProvider,
        IChartHubServerApiClient serverApiClient,
        AppGlobalSettings globalSettings,
        Func<Action, Task>? uiInvoke = null)
    {
        _googleAuthProvider = googleAuthProvider;
        _serverApiClient = serverApiClient;
        _globalSettings = globalSettings;
        _uiInvoke = uiInvoke ?? (async action => await Dispatcher.UIThread.InvokeAsync(action));

        // Load cached token from settings if available
        LoadCachedToken();

        // Start in Unauthenticated until silent restore is attempted
        CurrentState = AuthSessionState.Unauthenticated;
    }

    /// <summary>
    /// Attempt silent restore from cached Google credentials.
    /// If successful and server exchange succeeds, transitions to Authenticated.
    /// If any step fails, stays Unauthenticated.
    /// Non-blocking; completes even on failure.
    /// </summary>
    public async Task AttemptSilentRestoreAsync()
    {
        try
        {
            CurrentState = AuthSessionState.Authenticating;

            // Try to restore cached Google credential
            UserCredential? cachedCredential = await _googleAuthProvider.TryAuthorizeSilentAsync(AuthScopes)
                .ConfigureAwait(false);

            if (cachedCredential is null)
            {
                CurrentState = AuthSessionState.Unauthenticated;
                return;
            }

            string googleIdToken = cachedCredential.Token.IdToken;
            if (string.IsNullOrWhiteSpace(googleIdToken))
            {
                await _googleAuthProvider.SignOutAsync(cachedCredential).ConfigureAwait(false);
                CurrentState = AuthSessionState.Unauthenticated;
                return;
            }

            // Exchange Google token for server JWT
            string baseUrl = _globalSettings.ServerApiBaseUrl?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                CurrentState = AuthSessionState.Unauthenticated;
                return;
            }

            ChartHubServerAuthExchangeResponse exchange = await _serverApiClient
                .ExchangeGoogleTokenAsync(baseUrl, googleIdToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(exchange.AccessToken))
            {
                CurrentState = AuthSessionState.Unauthenticated;
                return;
            }

            // Decode JWT to extract email and expiry
            (string? email, DateTime expiryUtc) = DecodeJwtToken(exchange.AccessToken);
            if (string.IsNullOrWhiteSpace(email))
            {
                CurrentState = AuthSessionState.Unauthenticated;
                return;
            }

            // Set token and transition to Authenticated
            await SetAuthenticatedStateAsync(email, exchange.AccessToken, expiryUtc).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError("Auth", "Silent restore failed (expected if no cached creds)", ex);
            CurrentState = AuthSessionState.Unauthenticated;
        }
    }

    /// <summary>
    /// Initiate interactive sign-in via Google OAuth.
    /// On success, exchanges for server JWT and transitions to Authenticated.
    /// On failure, stays Unauthenticated.
    /// </summary>
    public async Task SignInAsync()
    {
        try
        {
            CurrentState = AuthSessionState.Authenticating;

            string baseUrl = _globalSettings.ServerApiBaseUrl?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                CurrentState = AuthSessionState.Unauthenticated;
                throw new InvalidOperationException(
                    "ChartHub Server URL is not configured. Set Runtime.ServerApiBaseUrl in Settings before signing in.");
            }

            // Interactive Google sign-in
            UserCredential credential = await _googleAuthProvider.AuthorizeInteractiveAsync(AuthScopes)
                .ConfigureAwait(false);

            string googleIdToken = credential.Token.IdToken;
            if (string.IsNullOrWhiteSpace(googleIdToken))
            {
                throw new InvalidOperationException("Google sign-in succeeded but did not return an ID token.");
            }

            // Exchange Google token for server JWT
            ChartHubServerAuthExchangeResponse exchange = await _serverApiClient
                .ExchangeGoogleTokenAsync(baseUrl, googleIdToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(exchange.AccessToken))
            {
                throw new InvalidOperationException("ChartHub Server returned an empty access token.");
            }

            // Decode JWT to extract email and expiry
            (string? email, DateTime expiryUtc) = DecodeJwtToken(exchange.AccessToken);
            if (string.IsNullOrWhiteSpace(email))
            {
                throw new InvalidOperationException("ChartHub Server JWT does not contain an email claim.");
            }

            // Set token and transition to Authenticated
            await SetAuthenticatedStateAsync(email, exchange.AccessToken, expiryUtc).ConfigureAwait(false);
        }
        catch (ChartHubServerApiException ex) when (ex.StatusCode == HttpStatusCode.BadRequest &&
                                                      string.Equals(ex.ErrorCode, "invalid_google_id_token",
                                                          StringComparison.OrdinalIgnoreCase))
        {
            await _uiInvoke(() =>
            {
                Logger.LogError("Auth", "Google token rejected by server", ex);
            }).ConfigureAwait(false);
            CurrentState = AuthSessionState.Unauthenticated;
            throw new InvalidOperationException(
                "Google sign-in token was rejected by ChartHub Server. Verify Google OAuth client IDs are aligned between app and server.",
                ex);
        }
        catch (ChartHubServerApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            await _uiInvoke(() =>
            {
                Logger.LogError("Auth", "Email not allowlisted in ChartHub Server", ex);
            }).ConfigureAwait(false);
            CurrentState = AuthSessionState.Unauthenticated;
            throw new InvalidOperationException(
                "Your Google account is not allowlisted by this ChartHub Server.",
                ex);
        }
        catch (ChartHubServerApiException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            await _uiInvoke(() =>
            {
                Logger.LogError("Auth", "ChartHub Server could not reach Google validation service", ex);
            }).ConfigureAwait(false);
            CurrentState = AuthSessionState.Unauthenticated;
            throw new InvalidOperationException(
                "ChartHub Server could not validate Google credentials right now. Try again shortly.",
                ex);
        }
        catch (ChartHubServerApiException ex)
        {
            await _uiInvoke(() =>
            {
                Logger.LogError("Auth", "Server token exchange failed", ex);
            }).ConfigureAwait(false);
            CurrentState = AuthSessionState.Unauthenticated;

            string serverMessage = string.IsNullOrWhiteSpace(ex.Message)
                ? "Unknown server error"
                : ex.Message;

            throw new InvalidOperationException(
                $"ChartHub Server token exchange failed: {serverMessage}",
                ex);
        }
        catch (Exception ex)
        {
            await _uiInvoke(() =>
            {
                Logger.LogError("Auth", "Sign-in failed", ex);
            }).ConfigureAwait(false);
            CurrentState = AuthSessionState.Unauthenticated;
            throw;
        }
    }

    /// <summary>
    /// Clear session and transition to Unauthenticated.
    /// Also clears Google cached credentials.
    /// </summary>
    public async Task SignOutAsync()
    {
        try
        {
            // Clear settings token
            _globalSettings.ServerApiAuthToken = string.Empty;

            // Clear properties
            CurrentAccessToken = null;
            SignedInEmail = null;
            _tokenExpiryUtc = DateTime.MinValue;

            CurrentState = AuthSessionState.Unauthenticated;
        }
        catch (Exception ex)
        {
            Logger.LogError("Auth", "Sign-out failed", ex);
        }
    }

    /// <summary>
    /// Check if token is still valid locally (not expired).
    /// Does not contact server.
    /// </summary>
    public bool IsTokenValidLocally()
    {
        if (CurrentState != AuthSessionState.Authenticated)
        {
            return false;
        }

        lock (_stateLock)
        {
            // Add 10-second buffer to catch tokens about to expire
            return DateTime.UtcNow < (_tokenExpiryUtc.AddSeconds(-10));
        }
    }

    /// <summary>
    /// Attempt to refresh token via silent Google restore + server exchange.
    /// Called when server returns 401 during a gated operation.
    /// If successful, transitions to Authenticated.
    /// If any step fails, transitions to Unauthenticated.
    /// </summary>
    public async Task AttemptSilentRefreshAsync()
    {
        try
        {
            CurrentState = AuthSessionState.Authenticating;

            // Try to restore cached Google credential
            UserCredential? cachedCredential = await _googleAuthProvider.TryAuthorizeSilentAsync(AuthScopes)
                .ConfigureAwait(false);

            if (cachedCredential is null)
            {
                CurrentState = AuthSessionState.Unauthenticated;
                return;
            }

            string googleIdToken = cachedCredential.Token.IdToken;
            if (string.IsNullOrWhiteSpace(googleIdToken))
            {
                await _googleAuthProvider.SignOutAsync(cachedCredential).ConfigureAwait(false);
                CurrentState = AuthSessionState.Unauthenticated;
                return;
            }

            // Exchange Google token for server JWT
            string baseUrl = _globalSettings.ServerApiBaseUrl?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                CurrentState = AuthSessionState.Unauthenticated;
                return;
            }

            ChartHubServerAuthExchangeResponse exchange = await _serverApiClient
                .ExchangeGoogleTokenAsync(baseUrl, googleIdToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(exchange.AccessToken))
            {
                CurrentState = AuthSessionState.Unauthenticated;
                return;
            }

            // Decode JWT
            (string? email, DateTime expiryUtc) = DecodeJwtToken(exchange.AccessToken);
            if (string.IsNullOrWhiteSpace(email))
            {
                CurrentState = AuthSessionState.Unauthenticated;
                return;
            }

            await SetAuthenticatedStateAsync(email, exchange.AccessToken, expiryUtc).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError("Auth", "Silent refresh failed", ex);
            CurrentState = AuthSessionState.Unauthenticated;
        }
    }

    /// <summary>
    /// Extract email and expiry from JWT without verifying signature.
    /// Used only after server has already validated the token.
    /// </summary>
    private static (string? email, DateTime expiryUtc) DecodeJwtToken(string accessToken)
    {
        try
        {
            string[] parts = accessToken.Split('.');
            if (parts.Length < 2)
            {
                return (null, DateTime.MinValue);
            }

            string payloadPart = parts[1]
                .Replace('-', '+')
                .Replace('_', '/');

            switch (payloadPart.Length % 4)
            {
                case 2:
                    payloadPart += "==";
                    break;
                case 3:
                    payloadPart += "=";
                    break;
            }

            byte[] payloadBytes = Convert.FromBase64String(payloadPart);
            string payloadJson = Encoding.UTF8.GetString(payloadBytes);

            using var doc = JsonDocument.Parse(payloadJson);
            JsonElement root = doc.RootElement;

            string? email = null;
            if (root.TryGetProperty("email", out JsonElement emailElement) && emailElement.ValueKind == JsonValueKind.String)
            {
                email = emailElement.GetString();
            }

            DateTime expiryUtc = DateTime.MinValue;
            if (root.TryGetProperty("exp", out JsonElement expElement) && expElement.ValueKind == JsonValueKind.Number)
            {
                long expSeconds = expElement.GetInt64();
                expiryUtc = DateTimeOffset.FromUnixTimeSeconds(expSeconds).UtcDateTime;
            }

            return (email, expiryUtc);
        }
        catch (Exception ex)
        {
            Logger.LogError("Auth", "Failed to decode JWT token", ex);
            return (null, DateTime.MinValue);
        }
    }

    /// <summary>
    /// Load cached token from settings (if any).
    /// Used at startup to restore token without full silent restore.
    /// </summary>
    private void LoadCachedToken()
    {
        try
        {
            string cachedToken = _globalSettings.ServerApiAuthToken?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(cachedToken))
            {
                return;
            }

            (string? email, DateTime expiryUtc) = DecodeJwtToken(cachedToken);
            if (string.IsNullOrWhiteSpace(email))
            {
                _globalSettings.ServerApiAuthToken = string.Empty;
                return;
            }

            // Only restore if token is still valid
            if (DateTime.UtcNow >= expiryUtc.AddSeconds(-10))
            {
                _globalSettings.ServerApiAuthToken = string.Empty;
                return;
            }

            _currentAccessToken = cachedToken;
            _signedInEmail = email;
            _tokenExpiryUtc = expiryUtc;
        }
        catch (Exception ex)
        {
            Logger.LogError("Auth", "Failed to load cached token", ex);
            _globalSettings.ServerApiAuthToken = string.Empty;
        }
    }

    /// <summary>
    /// Transition to Authenticated state and persist token.
    /// </summary>
    private async Task SetAuthenticatedStateAsync(string email, string accessToken, DateTime expiryUtc)
    {
        CurrentAccessToken = accessToken;
        SignedInEmail = email;

        lock (_stateLock)
        {
            _tokenExpiryUtc = expiryUtc;
        }

        // Persist token
        await _uiInvoke(() =>
        {
            _globalSettings.ServerApiAuthToken = accessToken;
        }).ConfigureAwait(false);

        CurrentState = AuthSessionState.Authenticated;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }
}
