using System.ComponentModel;

namespace ChartHub.Services;

/// <summary>
/// Manages authentication session state for ChartHub Server.
/// Handles Google OAuth → Server JWT exchange, token validation, and state transitions.
/// </summary>
public interface IAuthSessionService : INotifyPropertyChanged
{
    /// <summary>Current session state.</summary>
    AuthSessionState CurrentState { get; }

    /// <summary>Signed-in user email (only set when CurrentState is Authenticated).</summary>
    string? SignedInEmail { get; }

    /// <summary>Current server JWT access token (only valid when CurrentState is Authenticated).</summary>
    string? CurrentAccessToken { get; }

    /// <summary>
    /// Attempt to silently restore authentication from cached Google credentials.
    /// Called at app startup. Non-blocking; does not show UI.
    /// </summary>
    Task AttemptSilentRestoreAsync();

    /// <summary>
    /// Initiate interactive sign-in (opens Google OAuth dialog).
    /// Updates state to Authenticating, then Authenticated/Unauthenticated on completion.
    /// </summary>
    Task SignInAsync();

    /// <summary>
    /// Sign out and clear session.
    /// Updates state to Unauthenticated and clears cached credentials.
    /// </summary>
    Task SignOutAsync();

    /// <summary>
    /// Validate token locally (check expiry). Does not contact server.
    /// Returns true if token exists and is not expired locally.
    /// </summary>
    bool IsTokenValidLocally();

    /// <summary>
    /// Attempt to refresh token via silent Google credential restore + server exchange.
    /// Used when server returns 401 during a gated operation.
    /// </summary>
    Task AttemptSilentRefreshAsync();

    /// <summary>Raised when CurrentState changes.</summary>
    event EventHandler? SessionStateChanged;
}

/// <summary>Session state machine states.</summary>
public enum AuthSessionState
{
    /// <summary>Initial state; startup phase, checking cache.</summary>
    Unknown = 0,

    /// <summary>User is not authenticated.</summary>
    Unauthenticated = 1,

    /// <summary>Sign-in in progress (user interacting with Google OAuth or server exchange).</summary>
    Authenticating = 2,

    /// <summary>User is authenticated; token is valid.</summary>
    Authenticated = 3,

    /// <summary>Token exists but failed validation (expired, invalid signature, etc).</summary>
    Expired = 4,
}
