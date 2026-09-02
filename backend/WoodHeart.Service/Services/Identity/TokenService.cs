using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using WoodHeart.Domain.Entity.Identity;
using WoodHeart.Domain.Helpers;
using WoodHeart.Domain.Settings;
using WoodHeart.Service.Interfaces.Identity;

namespace WoodHeart.Service.Services.Identity;

/// <inheritdoc />
public class TokenService : ITokenService
{
    private readonly JwtSettings _settings;
    private readonly IDateTimeProvider _clock;
    private readonly SigningCredentials _credentials;
    private readonly JsonWebTokenHandler _handler = new();

    public TokenService(IOptions<JwtSettings> options, IDateTimeProvider clock)
    {
        _settings = options.Value;
        _clock = clock;

        // Built once. Deriving the key per request is measurable on a login
        // endpoint and buys nothing — the key does not change while the process
        // is alive, because changing it invalidates every issued token.
        _credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SigningKey)),
            SecurityAlgorithms.HmacSha256);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <b>The claim types are the long WS-Federation URIs on purpose.</b>
    /// <c>ClaimTypes.NameIdentifier</c> and <c>ClaimTypes.Role</c> are what
    /// <c>CurrentUserService</c> reads and what <c>RequireRole</c> checks
    /// against, and they survive the JWT handler's inbound claim mapping
    /// untouched. Emitting the short JWT names (<c>sub</c>, <c>role</c>)
    /// instead makes correctness depend on <c>MapInboundClaims</c> staying at
    /// its default — and if that ever flips, every authorization check silently
    /// starts failing closed.
    /// </para>
    /// <para>
    /// Roles are baked into the token, which is the usual trade: authorization
    /// costs no database round trip, and a role revoked mid-session survives
    /// until the access token expires. Fifteen minutes is the size of that
    /// window, and it is why <c>AccessTokenMinutes</c> is short.
    /// </para>
    /// </remarks>
    public AccessToken CreateAccessToken(AppUser user, IEnumerable<string> roles)
    {
        var issuedAt = _clock.UtcNow;
        var expiresAt = issuedAt.AddMinutes(_settings.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, user.UserName ?? string.Empty),

            // A unique id per token, so a future denylist can name one token
            // rather than having to revoke a whole user.
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("n"))
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _settings.Issuer,
            Audience = _settings.Audience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = _credentials
        };

        return new AccessToken(_handler.CreateToken(descriptor), expiresAt);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 256 bits from the cryptographic generator, base64url-encoded so it
    /// survives a cookie, a header and a URL without escaping. The plaintext
    /// exists only in the response; the database stores a SHA-256 of it, so a
    /// dump of the token table is not a set of usable sessions.
    /// </remarks>
    public string CreateRefreshToken() =>
        Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
}
