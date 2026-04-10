namespace ChartHub.Server.Contracts;

public sealed class AuthExchangeRequest
{
    public required string GoogleIdToken { get; init; }
}
