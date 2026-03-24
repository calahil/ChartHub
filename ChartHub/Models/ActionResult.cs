using System;

namespace ChartHub.Models;

/// <summary>
/// Represents the result of an action (Retry, Install, OpenFolder) performed on a queue item.
/// </summary>
public sealed class ActionResult : IEquatable<ActionResult>
{
    /// <summary>
    /// Type of action that was performed or failed.
    /// </summary>
    public required ActionType ActionType { get; init; }

    /// <summary>
    /// Status of the action result.
    /// </summary>
    public required ActionResultStatus Status { get; init; }

    /// <summary>
    /// Human-readable message describing the result.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// When the action was initiated (UTC).
    /// </summary>
    public DateTimeOffset InitiatedAtUtc { get; init; }

    /// <summary>
    /// When the action result was received (UTC). May differ from InitiatedAtUtc if action takes time.
    /// </summary>
    public DateTimeOffset CompletedAtUtc { get; init; }

    /// <summary>
    /// Duration in milliseconds from initiation to completion.
    /// </summary>
    public int DurationMs => (int)(CompletedAtUtc - InitiatedAtUtc).TotalMilliseconds;

    /// <summary>
    /// Display text for the UI (e.g., "Install requested" or "Retry failed").
    /// </summary>
    public string DisplayText => Status switch
    {
        ActionResultStatus.Pending => $"{ActionTypeText(ActionType)} in progress...",
        ActionResultStatus.Success => $"{ActionTypeText(ActionType)} completed",
        ActionResultStatus.Failed => $"{ActionTypeText(ActionType)} failed",
        _ => string.Empty
    };

    /// <summary>
    /// Short status indicator for badges or icons.
    /// </summary>
    public string StatusBadge => Status switch
    {
        ActionResultStatus.Pending => "⏳",
        ActionResultStatus.Success => "✓",
        ActionResultStatus.Failed => "✗",
        _ => "?"
    };

    private static string ActionTypeText(ActionType actionType) => actionType switch
    {
        ActionType.Retry => "Retry",
        ActionType.Install => "Install",
        ActionType.OpenFolder => "Open folder",
        ActionType.Push => "Push to desktop",
        _ => "Action"
    };

    public bool Equals(ActionResult? other) =>
        other is not null &&
        ActionType == other.ActionType &&
        Status == other.Status &&
        Message == other.Message &&
        InitiatedAtUtc == other.InitiatedAtUtc &&
        CompletedAtUtc == other.CompletedAtUtc;

    public override bool Equals(object? obj) => Equals(obj as ActionResult);
    public override int GetHashCode() => HashCode.Combine(ActionType, Status, Message, InitiatedAtUtc, CompletedAtUtc);
}

public enum ActionType
{
    Retry,
    Install,
    OpenFolder,
    Push
}

public enum ActionResultStatus
{
    Pending,
    Success,
    Failed
}
