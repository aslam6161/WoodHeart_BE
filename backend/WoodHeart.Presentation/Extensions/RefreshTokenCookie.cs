namespace WoodHeart.Presentation.Extensions;

/// <summary>
/// The refresh token's cookie: where the session actually lives.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a cookie and not the response body.</b> The alternative is handing
/// the refresh token to JavaScript and asking it to keep it in
/// <c>localStorage</c>. That storage is readable by every script on the page,
/// so one cross-site scripting hole — a compromised analytics tag, a bad
/// dependency, a review field that renders unescaped — hands over a thirty-day
/// session that survives the page being closed. An <c>HttpOnly</c> cookie is
/// not readable by script at all: the same XSS can still make requests as the
/// user, but it cannot walk away with the session.
/// </para>
/// <para>
/// <b>Every flag here is doing work.</b> <c>HttpOnly</c> is the whole point.
/// <c>Secure</c> keeps it off plaintext connections — browsers make an
/// exception for <c>localhost</c>, so development is unaffected.
/// <c>SameSite=Strict</c> means another site cannot cause the browser to send
/// it at all, which is what removes cross-site request forgery from the refresh
/// endpoint rather than mitigating it. And <see cref="Path"/> narrows it to the
/// account endpoints, so the token is not attached to the hundreds of catalog
/// and admin requests that have no use for it.
/// </para>
/// <para>
/// The access token goes the other way — in the response body, held in memory
/// by the Angular app, never persisted. It lasts fifteen minutes, so losing it
/// on a page refresh costs one silent call to <c>/api/account/refresh</c>.
/// </para>
/// </remarks>
public static class RefreshTokenCookie
{
    public const string Name = "wh_rt";

    /// <summary>
    /// Scoped to the endpoints that consume it.
    /// </summary>
    /// <remarks>
    /// Must stay in step with <c>AccountController</c>'s route. A mismatch is
    /// silent in the worst way: sign-in appears to work and every refresh
    /// afterwards fails, because the browser never sends a cookie whose path
    /// does not prefix the request.
    /// </remarks>
    public const string Path = "/api/account";

    public static void SetRefreshToken(
        this HttpResponse response, string token, DateTimeOffset expiresAt) =>
        response.Cookies.Append(Name, token, Options(expiresAt));

    /// <summary>
    /// Clears the cookie on sign-out.
    /// </summary>
    /// <remarks>
    /// The options must match those used to set it — same path, same
    /// <c>SameSite</c>, same <c>Secure</c> — or the browser deletes nothing and
    /// keeps sending the old cookie. It is a revoked token by then, so the
    /// failure is not a security hole, but it does make the next sign-in look
    /// like a session that will not die.
    /// </remarks>
    public static void ClearRefreshToken(this HttpResponse response) =>
        response.Cookies.Delete(Name, Options(DateTimeOffset.UnixEpoch));

    public static string? ReadRefreshToken(this HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var token) ? token : null;

    private static CookieOptions Options(DateTimeOffset expiresAt) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = Path,
        Expires = expiresAt,
        IsEssential = true
    };
}
