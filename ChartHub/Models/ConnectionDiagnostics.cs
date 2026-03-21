using System;

namespace ChartHub.Models;

/// <summary>
/// Captures detailed diagnostic information about the sync connection state, including
/// server version/health, last error details, and user-actionable remediation hints.
/// </summary>
public sealed class ConnectionDiagnostics
{
    /// <summary>
    /// Time of last connection attempt (success or failure).
    /// </summary>
    public DateTime? LastAttemptUtc { get; init; }

    /// <summary>
    /// Server API name and version (e.g., "ingestion-sync v1.0.0"). Populated after successful version check.
    /// </summary>
    public string? ServerInfo { get; init; }

    /// <summary>
    /// Full exception message from the last connection failure. Null if never failed.
    /// </summary>
    public string? LastErrorMessage { get; init; }

    /// <summary>
    /// Classification of the last error for quick diagnostics.
    /// </summary>
    public ErrorCategory? LastErrorCategory { get; init; }

    /// <summary>
    /// Human-readable remediation hint (e.g., "Check desktop URL is reachable", "Regenerate token in Settings").
    /// </summary>
    public string? RemediationHint { get; init; }

    /// <summary>
    /// True if the last connection was successful and server is reachable.
    /// </summary>
    public bool IsHealthy => ServerInfo is not null && LastErrorMessage is null;

    /// <summary>
    /// Summary text for UI display combining status and remediation.
    /// </summary>
    public string DiagnosticsSummary => IsHealthy
        ? $"✓ Connected to {ServerInfo}"
        : LastErrorCategory switch
        {
            ErrorCategory.NetworkUnreachable => $"✗ Desktop host unreachable. {RemediationHint}",
            ErrorCategory.AuthenticationFailed => $"✗ Invalid token or authentication failed. {RemediationHint}",
            ErrorCategory.UnsupportedVersion => $"✗ Server does not support ingestion sync. {RemediationHint}",
            ErrorCategory.UnknownError => $"✗ Connection failed: {LastErrorMessage}",
            _ => "⚠ No connection attempted yet."
        };

    /// <summary>
    /// Timestamp display in local timezone showing when last attempt/success occurred.
    /// </summary>
    public string LastAttemptDisplay => LastAttemptUtc.HasValue
        ? LastAttemptUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
        : "Never";
}

public enum ErrorCategory
{
    /// <summary>
    /// Network is unreachable (host not found, timeout, connection refused).
    /// </summary>
    NetworkUnreachable,

    /// <summary>
    /// Token/credentials invalid or expired.
    /// </summary>
    AuthenticationFailed,

    /// <summary>
    /// Server is reachable but doesn't support required features.
    /// </summary>
    UnsupportedVersion,

    /// <summary>
    /// Other error (unclassified).
    /// </summary>
    UnknownError
}
