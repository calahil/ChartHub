using ChartHub.Server.Options;

using Google.Apis.Auth;

using Microsoft.Extensions.Options;

namespace ChartHub.Server.Services;

public sealed class GoogleIdTokenValidator(IOptions<GoogleAuthOptions> options) : IGoogleIdTokenValidator
{
    private readonly GoogleAuthOptions _options = options.Value;

    public async Task<GoogleUserIdentity> ValidateAsync(string idToken, CancellationToken cancellationToken)
    {
        GoogleJsonWebSignature.ValidationSettings validationSettings = new()
        {
            Audience = _options.AllowedAudiences.Length > 0 ? _options.AllowedAudiences : null,
        };

        GoogleJsonWebSignature.Payload payload = await GoogleJsonWebSignature
            .ValidateAsync(idToken, validationSettings)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(payload.Email))
        {
            throw new InvalidOperationException("Google token did not include an email claim.");
        }

        return new GoogleUserIdentity(payload.Email);
    }
}
