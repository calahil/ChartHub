using ChartHub.Server.Contracts;
using ChartHub.Server.Options;
using ChartHub.Server.Services;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ChartHub.Server.Endpoints;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/exchange", async (
                [FromBody] AuthExchangeRequest request,
                IGoogleIdTokenValidator tokenValidator,
                IJwtTokenIssuer jwtTokenIssuer,
                IOptions<AuthOptions> authOptions,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.GoogleIdToken))
                {
                    return Results.BadRequest(new { error = "google_id_token_required" });
                }

                GoogleUserIdentity user = await tokenValidator
                    .ValidateAsync(request.GoogleIdToken, cancellationToken)
                    .ConfigureAwait(false);

                bool allowlisted = authOptions.Value.AllowedEmails.Any(email =>
                    string.Equals(email.Trim(), user.Email, StringComparison.OrdinalIgnoreCase));
                if (!allowlisted)
                {
                    return Results.Forbid();
                }

                DateTimeOffset expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(authOptions.Value.AccessTokenMinutes);
                string accessToken = jwtTokenIssuer.CreateAccessToken(user.Email, expiresAtUtc);

                return Results.Ok(new AuthExchangeResponse
                {
                    AccessToken = accessToken,
                    ExpiresAtUtc = expiresAtUtc,
                });
            })
            .WithName("ExchangeGoogleToken")
            .WithSummary("Exchange Google ID token for ChartHub JWT");

        return group;
    }
}
