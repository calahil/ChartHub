using ChartHub.Server.Contracts;
using ChartHub.Server.Options;
using ChartHub.Server.Services;

using Google.Apis.Auth;

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
                ILogger<AuthEndpointLogCategory> logger,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.GoogleIdToken))
                {
                    return Results.BadRequest(new { error = "google_id_token_required" });
                }

                GoogleUserIdentity user;

                try
                {
                    user = await tokenValidator
                        .ValidateAsync(request.GoogleIdToken, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (InvalidJwtException exception)
                {
                    AuthEndpointLog.InvalidGoogleToken(logger, exception);
                    return Results.BadRequest(new { error = "invalid_google_id_token" });
                }
                catch (InvalidOperationException exception)
                {
                    AuthEndpointLog.InvalidGoogleTokenPayload(logger, exception);
                    return Results.BadRequest(new { error = "invalid_google_id_token_payload" });
                }
                catch (HttpRequestException exception)
                {
                    AuthEndpointLog.GoogleValidationServiceUnavailable(logger, exception);
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                }
                catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
                {
                    AuthEndpointLog.GoogleValidationTimedOut(logger, exception);
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                }

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
            .WithSummary("Exchange Google ID token for ChartHub JWT")
            .Produces<AuthExchangeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireRateLimiting("auth");

        return group;
    }

    private static class AuthEndpointLog
    {
        private static readonly Action<ILogger, Exception?> InvalidGoogleTokenMessage =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(1001, nameof(InvalidGoogleToken)),
                "Auth exchange rejected due to invalid Google ID token.");

        private static readonly Action<ILogger, Exception?> InvalidGoogleTokenPayloadMessage =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(1002, nameof(InvalidGoogleTokenPayload)),
                "Auth exchange rejected due to invalid Google token payload.");

        private static readonly Action<ILogger, Exception?> GoogleValidationServiceUnavailableMessage =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(1003, nameof(GoogleValidationServiceUnavailable)),
                "Auth exchange could not reach Google token validation service.");

        private static readonly Action<ILogger, Exception?> GoogleValidationTimedOutMessage =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(1004, nameof(GoogleValidationTimedOut)),
                "Auth exchange token validation timed out.");

        public static void InvalidGoogleToken(ILogger logger, Exception exception) =>
            InvalidGoogleTokenMessage(logger, exception);

        public static void InvalidGoogleTokenPayload(ILogger logger, Exception exception) =>
            InvalidGoogleTokenPayloadMessage(logger, exception);

        public static void GoogleValidationServiceUnavailable(ILogger logger, Exception exception) =>
            GoogleValidationServiceUnavailableMessage(logger, exception);

        public static void GoogleValidationTimedOut(ILogger logger, Exception exception) =>
            GoogleValidationTimedOutMessage(logger, exception);
    }

    private sealed class AuthEndpointLogCategory;
}
