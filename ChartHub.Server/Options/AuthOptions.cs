namespace ChartHub.Server.Options;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string JwtSigningKey { get; set; } = "CHANGE_ME_WITH_A_32_CHAR_MINIMUM_SECRET";

    public string Issuer { get; set; } = "charthub-server";

    public string Audience { get; set; } = "charthub-clients";

    public int AccessTokenMinutes { get; set; } = 60;

    public List<string> AllowedEmails { get; set; } = [];
}
