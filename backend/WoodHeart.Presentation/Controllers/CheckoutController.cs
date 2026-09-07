using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WoodHeart.Domain.Constants;
using WoodHeart.Service.DTOs.Ordering;
using WoodHeart.Service.Interfaces.Ordering;

namespace WoodHeart.Presentation.Controllers;

/// <summary>
/// Turning a basket into an order.
/// </summary>
/// <remarks>
/// <para>
/// <c>[AllowAnonymous]</c>, like the cart, and for the same reason: guest
/// checkout is the main path in this market, not an edge case. A signed-in
/// customer gets their orders attached to their account; everyone else gets an
/// order all the same.
/// </para>
/// <para>
/// <b>No amount of money is accepted from the client.</b> The request carries a
/// contact, an address and a payment method; every figure is recomputed
/// server-side from the caller's own cart.
/// </para>
/// </remarks>
[AllowAnonymous]
// Queued rather than rejected. A customer who hits the limit at the moment of
// paying must wait, never be turned away — a 429 on this route is a lost sale.
[EnableRateLimiting(RateLimitPolicies.Checkout)]
[Route("api/checkout")]
public class CheckoutController(ICheckoutService checkout) : BaseApiController
{
    /// <summary>The payment methods this basket may use, with any surcharge.</summary>
    /// <remarks>
    /// The address is optional so the payment step can render while the form is
    /// still being filled in. Once it is supplied, the methods reflect where the
    /// order is actually going — cash on delivery to a remote district is a
    /// decision the shop gets to make.
    /// </remarks>
    [HttpPost("payment-methods")]
    public async Task<IActionResult> PaymentMethods(
        [FromBody] DeliveryAddressDto? address, CancellationToken cancellationToken) =>
        HandleResult(await checkout.GetPaymentMethodsAsync(address, cancellationToken));

    /// <summary>
    /// Places the order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Send an <c>Idempotency-Key</c> header. A customer on a slow connection
    /// presses the button twice, and a browser retries a POST that timed out
    /// after the order was already written — with the key, the second request
    /// returns the first order and sets <c>alreadyPlaced</c>. Without it, the
    /// second request places a second order, and somebody gets two sofas.
    /// </para>
    /// <para>
    /// Any redirect URL in the response belongs to the payment gateway. Cash on
    /// delivery returns none, and the storefront goes straight to the
    /// confirmation page.
    /// </para>
    /// </remarks>
    [HttpPost("place-order")]
    public async Task<IActionResult> PlaceOrder(
        [FromBody] PlaceOrderDto dto,
        [FromHeader(Name = GlobalConstants.IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken) =>
        HandleResult(await checkout.PlaceOrderAsync(dto, idempotencyKey, cancellationToken));
}
