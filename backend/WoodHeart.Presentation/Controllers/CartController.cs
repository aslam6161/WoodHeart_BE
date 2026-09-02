using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WoodHeart.Domain.Constants;
using WoodHeart.Service.DTOs.Ordering;
using WoodHeart.Service.Interfaces.Ordering;

namespace WoodHeart.Presentation.Controllers;

/// <summary>
/// The basket, for signed-in customers and guests alike.
/// </summary>
/// <remarks>
/// <para>
/// <c>[AllowAnonymous]</c> is the whole point rather than an oversight: guest
/// checkout is the main path here, so every one of these endpoints has to work
/// without an account. The visitor's identity comes from
/// <c>AnonymousIdMiddleware</c>, which mints an unguessable id on the first
/// request to this route and returns it as an HttpOnly cookie.
/// </para>
/// <para>
/// <b>No endpoint takes a cart id.</b> The service resolves the cart from the
/// caller — user id, or the hash of the guest cookie — so there is no request
/// shape that reads somebody else's basket. Line ids are accepted and checked
/// against the resolved cart before anything is touched.
/// </para>
/// <para>
/// Rate limited on the <c>Public</c> policy. Adding to a basket is ordinary
/// browsing behaviour and a customer comparing four sofas will hit it a dozen
/// times in a minute; the tight policy belongs on sign-in, not here.
/// </para>
/// </remarks>
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Public)]
[Route("api/cart")]
public class CartController(ICartService cart) : BaseApiController
{
    /// <summary>The caller's basket, priced.</summary>
    /// <remarks>
    /// Returns an empty basket rather than a 404 when there is none. "You have
    /// no basket yet" is the ordinary state of a first-time visitor, and a
    /// storefront header that has to interpret a 404 to render a zero is one
    /// that shows a spinner forever the first time anything else goes wrong.
    /// </remarks>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        HandleResult(await cart.GetAsync(cancellationToken));

    /// <summary>Adds a variant, or increases the quantity already there.</summary>
    [HttpPost("items")]
    public async Task<IActionResult> Add(
        [FromBody] AddToCartDto dto, CancellationToken cancellationToken) =>
        HandleResult(await cart.AddAsync(dto, cancellationToken));

    /// <summary>Sets one line's quantity. Zero removes the line.</summary>
    [HttpPut("items/{lineId:long}")]
    public async Task<IActionResult> UpdateLine(
        long lineId, [FromBody] UpdateCartLineDto dto, CancellationToken cancellationToken) =>
        HandleResult(await cart.UpdateLineAsync(lineId, dto, cancellationToken));

    [HttpDelete("items/{lineId:long}")]
    public async Task<IActionResult> RemoveLine(long lineId, CancellationToken cancellationToken) =>
        HandleResult(await cart.RemoveLineAsync(lineId, cancellationToken));

    [HttpDelete]
    public async Task<IActionResult> Clear(CancellationToken cancellationToken) =>
        HandleResult(await cart.ClearAsync(cancellationToken));

    /// <summary>
    /// Records where the order is going, which is what makes delivery
    /// priceable.
    /// </summary>
    /// <remarks>
    /// Until this is set the cart reports <c>deliveryPending</c> and a zero
    /// fee. That is deliberate — quoting a Dhaka rate to someone in Sylhet and
    /// then raising it at checkout is the surprise that loses the order.
    /// </remarks>
    [HttpPut("delivery-zone")]
    public async Task<IActionResult> SetDeliveryZone(
        [FromBody] SetDeliveryZoneDto dto, CancellationToken cancellationToken) =>
        HandleResult(await cart.SetDeliveryZoneAsync(dto, cancellationToken));
}
