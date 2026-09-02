using WoodHeart.Domain.Entity.Identity;
using WoodHeart.Repository;
using WoodHeart.Service.DTOs.Identity;

namespace WoodHeart.Service.Interfaces.Identity;

/// <summary>A signed access token and the moment it stops being valid.</summary>
public readonly record struct AccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>
/// Mints the tokens. Knows nothing about users beyond what goes in the claims.
/// </summary>
/// <remarks>
/// Separate from <see cref="IAccountService"/> so the signing details — the
/// algorithm, the claim types, the lifetime — live in one small testable class,
/// and so a future second issuer (an OTP flow, a partner integration) reuses it
/// rather than reimplementing the claim set slightly differently.
/// </remarks>
public interface ITokenService
{
    /// <summary>Signs a JWT carrying the user's id, phone number and roles.</summary>
    AccessToken CreateAccessToken(AppUser user, IEnumerable<string> roles);

    /// <summary>
    /// A new opaque refresh token.
    /// </summary>
    /// <remarks>
    /// Opaque and random rather than a second JWT. Nothing needs to read it —
    /// it is a lookup key into a table we control, which is what makes
    /// revocation possible at all. A self-describing token is valid until it
    /// expires no matter what the database says.
    /// </remarks>
    string CreateRefreshToken();
}

/// <summary>
/// Registration, sign-in and session lifetime.
/// </summary>
/// <remarks>
/// Every method returns the refresh token on the DTO rather than setting a
/// cookie. Cookies are an HTTP concept and this layer has no
/// <c>HttpContext</c> — the controller owns that translation, which is also
/// what keeps these methods testable without a web host.
/// </remarks>
public interface IAccountService
{
    Task<GeneralResponse<AuthenticatedUserDto>> LoginAsync(
        LoginDto dto, CancellationToken cancellationToken = default);

    Task<GeneralResponse<AuthenticatedUserDto>> RegisterAsync(
        RegisterDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchanges a refresh token for a new pair, rotating it.
    /// </summary>
    /// <remarks>
    /// The presented token is revoked and replaced on every call, so a token
    /// seen twice means it leaked. That case revokes the entire chain — see the
    /// implementation.
    /// </remarks>
    Task<GeneralResponse<AuthenticatedUserDto>> RefreshAsync(
        string? refreshToken, CancellationToken cancellationToken = default);

    /// <summary>Revokes one session. Idempotent, and succeeds for an unknown token.</summary>
    Task<GeneralResponse> LogoutAsync(
        string? refreshToken, CancellationToken cancellationToken = default);

    /// <summary>Revokes every live session for the current user.</summary>
    Task<GeneralResponse> LogoutEverywhereAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The signed-in user, re-read from the database.
    /// </summary>
    /// <remarks>
    /// Re-read rather than reflected back from the token, so a role granted or
    /// revoked since the token was issued is visible on the next page load
    /// instead of at the end of the token's lifetime.
    /// </remarks>
    Task<GeneralResponse<AuthenticatedUserDto>> GetCurrentAsync(
        CancellationToken cancellationToken = default);

    Task<GeneralResponse> ChangePasswordAsync(
        ChangePasswordDto dto, CancellationToken cancellationToken = default);
}
