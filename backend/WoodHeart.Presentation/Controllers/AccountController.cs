using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WoodHeart.Domain.Constants;
using WoodHeart.Presentation.Extensions;
using WoodHeart.Repository;
using WoodHeart.Service.DTOs.Identity;
using WoodHeart.Service.Interfaces.Identity;

namespace WoodHeart.Presentation.Controllers;

/// <summary>
/// Sign-in, registration and session lifetime.
/// </summary>
/// <remarks>
/// <para>
/// The one place in the API that touches cookies. Everything else is a bearer
/// token, and everything below is the same pattern as any other controller —
/// call a service, hand the result to <c>HandleResult</c> — with one addition:
/// the refresh token comes back on the DTO marked <c>[JsonIgnore]</c>, and
/// <see cref="Issue"/> moves it into an <c>HttpOnly</c> cookie before the body
/// is written. See <see cref="RefreshTokenCookie"/> for why.
/// </para>
/// <para>
/// <b>The whole controller is rate limited.</b> Login and registration are the
/// brute-force targets, and refresh is worth limiting too — an attacker holding
/// a stolen cookie should not get unlimited attempts to find a moment when
/// rotation races.
/// </para>
/// </remarks>
[Route("api/account")]
[EnableRateLimiting(RateLimitPolicies.Sensitive)]
public class AccountController(IAccountService accounts) : BaseApiController
{
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto dto, CancellationToken cancellationToken) =>
        Issue(await accounts.LoginAsync(dto, cancellationToken));

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto dto, CancellationToken cancellationToken) =>
        Issue(await accounts.RegisterAsync(dto, cancellationToken));

    /// <summary>Exchanges the refresh cookie for a new access token.</summary>
    /// <remarks>
    /// <para>
    /// Takes no body. The token is read from the cookie the browser attaches,
    /// which is the only place it exists — a client cannot send it explicitly
    /// because a client cannot read it.
    /// </para>
    /// <para>
    /// Anonymous, and necessarily so: this is the endpoint called precisely
    /// when the access token has expired, so requiring one would make it
    /// unreachable at the only moment it is needed.
    /// </para>
    /// </remarks>
    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        var result = await accounts.RefreshAsync(Request.ReadRefreshToken(), cancellationToken);

        // A refresh that fails ends the session, so the dead cookie goes too.
        // Leaving it means the client retries with it on every page load and
        // gets the same 401 forever.
        if (!result.IsSuccess)
        {
            Response.ClearRefreshToken();
        }

        return Issue(result);
    }

    /// <summary>Ends this session.</summary>
    /// <remarks>
    /// Anonymous on purpose. Someone whose access token has already expired is
    /// exactly who is trying to sign out, and refusing them would leave the
    /// refresh token live for another thirty days.
    /// </remarks>
    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var result = await accounts.LogoutAsync(Request.ReadRefreshToken(), cancellationToken);

        Response.ClearRefreshToken();

        return HandleResult(result);
    }

    /// <summary>Ends every session this user has, on every device.</summary>
    [Authorize]
    [HttpPost("logout-everywhere")]
    public async Task<IActionResult> LogoutEverywhere(CancellationToken cancellationToken)
    {
        var result = await accounts.LogoutEverywhereAsync(cancellationToken);

        Response.ClearRefreshToken();

        return HandleResult(result);
    }

    /// <summary>The signed-in user, re-read from the database.</summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken) =>
        HandleResult(await accounts.GetCurrentAsync(cancellationToken));

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordDto dto, CancellationToken cancellationToken)
    {
        var result = await accounts.ChangePasswordAsync(dto, cancellationToken);

        // Changing a password revokes every session including this one, so the
        // cookie is now pointing at a revoked token. Clearing it here is what
        // sends the customer to the sign-in screen rather than into a refresh
        // loop that cannot succeed.
        if (result.IsSuccess)
        {
            Response.ClearRefreshToken();
        }

        return HandleResult(result);
    }

    // -------------------------------------------------------------------------

    /// <summary>
    /// Moves the refresh token from the DTO into the cookie, then returns the body.
    /// </summary>
    /// <remarks>
    /// The property is <c>[JsonIgnore]</c>, so it would not be serialised even
    /// if this method were skipped — but it is nulled anyway. Two independent
    /// reasons a token cannot reach the body is the right number for the one
    /// value in this API that must not.
    /// </remarks>
    private IActionResult Issue(GeneralResponse<AuthenticatedUserDto> result)
    {
        if (result is { IsSuccess: true, Data: { RefreshToken: { } token } data })
        {
            Response.SetRefreshToken(token, data.RefreshTokenExpiresAt);

            data.RefreshToken = null;
        }

        return HandleResult(result);
    }
}
