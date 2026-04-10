using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

using ChartHub.Server.Options;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ChartHub.Server.Services;

public sealed class JwtTokenIssuer(IOptions<AuthOptions> options) : IJwtTokenIssuer
{
    private readonly AuthOptions _options = options.Value;

    public string CreateAccessToken(string email, DateTimeOffset expiresAtUtc)
    {
        SymmetricSecurityKey securityKey = new(Encoding.UTF8.GetBytes(_options.JwtSigningKey));
        SigningCredentials credentials = new(securityKey, SecurityAlgorithms.HmacSha256);

        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, email),
            new(JwtRegisteredClaimNames.Email, email),
            new(ClaimTypes.Email, email),
        ];

        JwtSecurityToken token = new(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expiresAtUtc.UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
