namespace WoodHeart.Domain.Settings;

/// <summary>Bound from the <c>Jwt</c> configuration section.</summary>
public class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "WoodHeart";

    public string Audience { get; set; } = "WoodHeart.Client";

    /// <summary>
    /// Never committed. Supplied by user-secrets locally and an environment
    /// variable in production; startup fails if it is shorter than 32
    /// characters, because a short HMAC key is a forgeable admin token.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Short-lived on purpose — the refresh token carries the session.</summary>
    public int AccessTokenMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 30;
}
