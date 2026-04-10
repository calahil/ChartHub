namespace ChartHub.Server.Contracts;

public sealed class AuthExchangeResponse
{
    public required string AccessToken { get; init; }

    public required DateTimeOffset ExpiresAtUtc { get; init; }
}
