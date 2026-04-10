namespace ChartHub.Server.Services;

public interface IJwtTokenIssuer
{
    string CreateAccessToken(string email, DateTimeOffset expiresAtUtc);
}
