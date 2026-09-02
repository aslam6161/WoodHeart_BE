using System.Text.Json;
using Microsoft.AspNetCore.Http;
using WoodHeart.Presentation.Extensions;
using WoodHeart.Service.DTOs.Identity;

namespace WoodHeart.Tests.Identity;

/// <summary>
/// The two guarantees that keep the refresh token out of reach of a script.
/// </summary>
/// <remarks>
/// Both are one-line properties that are easy to remove during a refactor and
/// impossible to notice missing: the response still contains a working session,
/// so every manual test passes. These are the tests that fail instead.
/// </remarks>
public class RefreshTokenCookieTests
{
    /// <summary>Matches the API's own camelCase policy, so the test sees real field names.</summary>
    private static readonly JsonSerializerOptions Json =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public void The_refresh_token_is_never_serialised_into_a_response_body()
    {
        var dto = new AuthenticatedUserDto
        {
            Id = 42,
            PhoneNumber = "+8801712345678",
            AccessToken = "the-access-token",
            RefreshToken = "the-refresh-token-that-must-not-escape",
            RefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        };

        var json = JsonSerializer.Serialize(dto, Json);

        // A body is readable by every script on the page. The cookie exists
        // precisely so a compromised script cannot walk away with a thirty-day
        // session, and a serialised copy in the body hands it back.
        json.ShouldNotContain("the-refresh-token-that-must-not-escape");
        json.ShouldNotContain("refreshToken", Case.Insensitive);

        // The access token does travel in the body — that is the design.
        json.ShouldContain("the-access-token");
    }

    [Fact]
    public void The_cookie_carries_every_flag_that_makes_it_worth_using()
    {
        var context = new DefaultHttpContext();

        context.Response.SetRefreshToken("a-token", DateTimeOffset.UtcNow.AddDays(30));

        var header = context.Response.Headers.SetCookie.ToString();

        header.ShouldContain($"{RefreshTokenCookie.Name}=a-token");

        // HttpOnly is the whole point: script cannot read it.
        header.ShouldContain("httponly", Case.Insensitive);

        // Secure keeps it off plaintext connections. Browsers exempt localhost,
        // so development is unaffected.
        header.ShouldContain("secure", Case.Insensitive);

        // SameSite=Strict removes cross-site request forgery from the refresh
        // endpoint rather than mitigating it.
        header.ShouldContain("samesite=strict", Case.Insensitive);

        // Scoped to the account endpoints, so it is not attached to the hundreds
        // of catalog and admin requests that have no use for it.
        header.ShouldContain($"path={RefreshTokenCookie.Path}", Case.Insensitive);
    }

    [Fact]
    public void Clearing_uses_the_same_path_it_was_set_with()
    {
        var context = new DefaultHttpContext();

        context.Response.ClearRefreshToken();

        var header = context.Response.Headers.SetCookie.ToString();

        // A delete whose attributes do not match the original deletes nothing,
        // and the browser keeps sending a revoked cookie on every page load.
        header.ShouldContain($"path={RefreshTokenCookie.Path}", Case.Insensitive);
        header.ShouldContain("expires=", Case.Insensitive);
    }

    [Fact]
    public void Reading_returns_null_rather_than_throwing_when_there_is_no_cookie()
    {
        // The common case on a first visit, and on every request from a client
        // that has signed out. It must be an ordinary "no session", not a 500.
        new DefaultHttpContext().Request.ReadRefreshToken().ShouldBeNull();
    }
}
