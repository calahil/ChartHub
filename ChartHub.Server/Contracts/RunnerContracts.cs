namespace ChartHub.Server.Contracts;

public sealed class IssueRunnerRegistrationTokenRequest
{
    /// <summary>Token time-to-live in minutes. Defaults to 15.</summary>
    public int TtlMinutes { get; init; } = 15;
}

public sealed class RunnerRegistrationTokenResponse
{
    public string TokenId { get; init; } = "";
    public string PlainToken { get; init; } = "";
    public DateTimeOffset ExpiresAtUtc { get; init; }

    public RunnerRegistrationTokenResponse() { }

    public RunnerRegistrationTokenResponse(string tokenId, string plainToken, DateTimeOffset expiresAtUtc)
    {
        TokenId = tokenId;
        PlainToken = plainToken;
        ExpiresAtUtc = expiresAtUtc;
    }
}

public sealed class RegisterRunnerRequest
{
    public string RunnerName { get; init; } = "";
    public string RegistrationToken { get; init; } = "";
    public string Secret { get; init; } = "";
    public int MaxConcurrency { get; init; } = 1;
}

public sealed class RegisterRunnerResponse
{
    public string RunnerId { get; init; } = "";

    public RegisterRunnerResponse() { }

    public RegisterRunnerResponse(string runnerId)
    {
        RunnerId = runnerId;
    }
}

public sealed class RunnerHeartbeatRequest
{
    public int ActiveJobCount { get; init; }
}

public sealed class RunnerSummaryResponse
{
    public string RunnerId { get; init; } = "";
    public string RunnerName { get; init; } = "";
    public int MaxConcurrency { get; init; }
    public DateTimeOffset RegisteredAtUtc { get; init; }
    public DateTimeOffset? LastHeartbeatUtc { get; init; }
    public int? LastActiveJobCount { get; init; }
    public bool IsActive { get; init; }
    public bool IsOnline { get; init; }
}

public sealed class TranscriptionJobResponse
{
    public string JobId { get; init; } = "";
    public string SongId { get; init; } = "";
    public string SongFolderPath { get; init; } = "";
    public string Aggressiveness { get; init; } = "";
    public int AttemptNumber { get; init; }
}

public sealed class TranscriptionJobFailRequest
{
    public string? Reason { get; init; }
}
