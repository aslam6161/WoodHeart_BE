using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Identity;
using WoodHeart.Domain.Settings;
using WoodHeart.Service.Services.Identity;
using WoodHeart.Tests.Helper;

namespace WoodHeart.Tests.Identity;

/// <summary>
/// What the access token actually contains, checked by reading it back.
/// </summary>
/// <remarks>
/// <para>
/// Every assertion here goes through a real <see cref="JsonWebTokenHandler"/>
/// validation rather than inspecting the descriptor we just built. Asserting on
/// the input proves only that the test and the code agree; validating the
/// signed string proves the token an actual client will present survives the
/// same middleware the API runs.
/// </para>
/// <para>
/// The claim-type test is the one that matters. <c>CurrentUserService</c> reads
/// <c>ClaimTypes.NameIdentifier</c> and <c>[Authorize(Roles = ...)]</c> checks
/// <c>ClaimTypes.Role</c>; emitting the short JWT names instead would leave
/// every request authenticated as nobody, with no role, and every admin
/// endpoint answering 403 for a perfectly valid token.
/// </para>
/// </remarks>
public class TokenServiceTests
{
    private const string SigningKey = "a-test-signing-key-long-enough-to-be-legal-32+";

    private readonly FakeClock _clock = new();

    private readonly JwtSettings _settings = new()
    {
        Issuer = "WoodHeart",
        Audience = "WoodHeart.Client",
        SigningKey = SigningKey,
        AccessTokenMinutes = 15,
        RefreshTokenDays = 30
    };

    private TokenService CreateService() => new(Options.Create(_settings), _clock);

    private static AppUser User => new()
    {
        Id = 42,
        UserName = "+8801712345678",
        FullName = "Test Admin"
    };

    private async Task<ClaimsPrincipal> ValidateAsync(string token)
    {
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            ValidIssuer = _settings.Issuer,
            ValidAudience = _settings.Audience,

            // The handler validates lifetime against the wall clock, and
            // FakeClock's "now" is a fixed date in the past.
            ValidateLifetime = false
        });

        result.IsValid.ShouldBeTrue(result.Exception?.Message);

        return new ClaimsPrincipal(result.ClaimsIdentity);
    }

    [Fact]
    public async Task Carries_the_user_id_and_phone_number_the_rest_of_the_app_reads()
    {
        var token = CreateService().CreateAccessToken(User, [Roles.Admin]);

        var principal = await ValidateAsync(token.Value);

        // Exactly the two claims CurrentUserService looks for. Change either
        // name and every request becomes anonymous with a valid token.
        principal.FindFirstValue(ClaimTypes.NameIdentifier).ShouldBe("42");
        principal.FindFirstValue(ClaimTypes.Name).ShouldBe("+8801712345678");
    }

    [Fact]
    public async Task Carries_every_role_in_the_type_that_authorization_checks()
    {
        var token = CreateService().CreateAccessToken(User, [Roles.Admin, Roles.Manager]);

        var principal = await ValidateAsync(token.Value);

        principal.IsInRole(Roles.Admin).ShouldBeTrue();
        principal.IsInRole(Roles.Manager).ShouldBeTrue();
        principal.IsInRole(Roles.Customer).ShouldBeFalse();
    }

    [Fact]
    public async Task Is_signed_with_the_configured_key_and_nothing_else()
    {
        var token = CreateService().CreateAccessToken(User, []);

        var wrongKey = await new JsonWebTokenHandler().ValidateTokenAsync(
            token.Value,
            new TokenValidationParameters
            {
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes("a-completely-different-key-also-32-chars")),
                ValidIssuer = _settings.Issuer,
                ValidAudience = _settings.Audience,
                ValidateLifetime = false
            });

        // The entire security model rests on this. A token signed by anyone
        // else is an Admin token if it validates.
        wrongKey.IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task Expires_after_the_configured_number_of_minutes()
    {
        _settings.AccessTokenMinutes = 5;

        var token = CreateService().CreateAccessToken(User, []);

        token.ExpiresAt.ShouldBe(_clock.UtcNow.AddMinutes(5));

        // And the same value is inside the signed token, not only on the record
        // we hand the client — a mismatch would have the client refreshing at
        // the wrong moment.
        var principal = await ValidateAsync(token.Value);
        var exp = principal.FindFirstValue(JwtRegisteredClaimNames.Exp);

        DateTimeOffset.FromUnixTimeSeconds(long.Parse(exp!))
            .ShouldBe(_clock.UtcNow.AddMinutes(5), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Gives_every_token_a_distinct_id()
    {
        var service = CreateService();

        var first = await ValidateAsync(service.CreateAccessToken(User, []).Value);
        var second = await ValidateAsync(service.CreateAccessToken(User, []).Value);

        // Same user, same second, same claims — so without an explicit jti the
        // two tokens would be byte-identical, and a future denylist could not
        // name one of them.
        first.FindFirstValue(JwtRegisteredClaimNames.Jti)
            .ShouldNotBe(second.FindFirstValue(JwtRegisteredClaimNames.Jti));
    }

    [Fact]
    public void Refresh_tokens_are_unique_and_safe_to_put_in_a_cookie()
    {
        var service = CreateService();

        var tokens = Enumerable.Range(0, 200).Select(_ => service.CreateRefreshToken()).ToList();

        tokens.Distinct(StringComparer.Ordinal).Count().ShouldBe(tokens.Count);

        foreach (var token in tokens)
        {
            // Base64url: no '+', '/' or '=' to be mangled by a cookie, a header
            // or a URL. A token that survives the round trip only sometimes is
            // an intermittent sign-out nobody can reproduce.
            token.ShouldNotContain("+");
            token.ShouldNotContain("/");
            token.ShouldNotContain("=");
            token.Length.ShouldBeGreaterThanOrEqualTo(40);
        }
    }
}
