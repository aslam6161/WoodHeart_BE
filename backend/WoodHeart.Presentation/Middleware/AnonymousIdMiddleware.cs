using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using WoodHeart.Domain.Constants;

namespace WoodHeart.Presentation.Middleware;

/// <summary>
/// Gives a signed-out visitor a stable id, so their basket survives a reload.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scoped to the paths that need it, not applied globally.</b> A cookie
/// minted on every request to every endpoint is a tracking cookie, and it would
/// tag someone who only ever read a product page. Restricting it to the cart
/// and checkout routes keeps it a functional cookie: it exists because the
/// visitor put something in a basket, which is the one thing that cannot work
/// without it.
/// </para>
/// <para>
/// <b>Nothing is minted for a signed-in user.</b> Their cart is found by user
/// id, and a second identity for the same person is a second cart to reconcile.
/// </para>
/// <para>
/// The value is written into the request headers as well as the response
/// cookie, because the service resolving the cart runs during <i>this</i>
/// request — waiting for the browser to send the cookie back would mean the
/// first "add to basket" of every session silently failed.
/// </para>
/// </remarks>
public class AnonymousIdMiddleware(RequestDelegate next)
{
    /// <summary>
    /// A year. The cookie is what a returning visitor's basket hangs from, and
    /// carts expire on their own (30 days) long before this does.
    /// </summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(365);

    private static readonly string[] Paths = ["/api/cart", "/api/checkout"];

    public async Task InvokeAsync(HttpContext context)
    {
        if (ShouldIssue(context))
        {
            Issue(context);
        }

        await next(context);
    }

    private static bool ShouldIssue(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            return false;
        }

        if (!Paths.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // An id already in hand, from either source, is left alone. Reissuing
        // would orphan the basket it identifies.
        return string.IsNullOrWhiteSpace(context.Request.Headers[GlobalConstants.AnonymousIdHeader])
               && !context.Request.Cookies.ContainsKey(GlobalConstants.AnonymousIdCookie);
    }

    private static void Issue(HttpContext context)
    {
        // 256 bits from a cryptographic source. This is a bearer credential for
        // a basket — and, after checkout, for a delivery address — so it has to
        // be unguessable rather than merely unique. A sequential id or a
        // timestamp would let anyone read the basket next door.
        var id = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

        context.Request.Headers[GlobalConstants.AnonymousIdHeader] = id;

        context.Response.Cookies.Append(GlobalConstants.AnonymousIdCookie, id, new CookieOptions
        {
            // Not readable from script: the value identifies a basket, and
            // there is nothing the Angular app needs to do with it that the
            // browser does not do automatically.
            HttpOnly = true,
            Secure = true,

            // Lax rather than Strict, unlike the refresh token. A customer
            // following a shared product link into the site should still have
            // their basket — Strict would drop the cookie on that first
            // cross-site navigation and quietly empty it.
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.Add(Lifetime),
            IsEssential = true,
            Path = "/"
        });
    }
}
