using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WoodHeart.Domain.Constants;
using WoodHeart.Service.DTOs.Ordering;
using WoodHeart.Service.Interfaces.Ordering;

namespace WoodHeart.Presentation.Controllers;

/// <summary>
/// A customer's own orders.
/// </summary>
/// <remarks>
/// <para>
/// <c>[Authorize]</c> by default: this is the signed-in customer's history, and
/// every read is scoped to their user id. The one exception is the guest
/// lookup below, which is opened deliberately and guarded differently.
/// </para>
/// <para>
/// Orders are addressed by their number rather than their id — it is what the
/// customer has in their confirmation SMS, and it keeps the database id out of
/// the URL bar.
/// </para>
/// </remarks>
[Authorize]
[EnableRateLimiting(RateLimitPolicies.Public)]
[Route("api/orders")]
public class OrdersController(IOrderService orders) : BaseApiController
{
    /// <summary>The caller's orders, newest first.</summary>
    [HttpGet]
    public async Task<IActionResult> Mine(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = OrderRules.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        HandleResult(await orders.GetMineAsync(page, pageSize, cancellationToken));

    /// <summary>One of the caller's own orders, in full.</summary>
    [HttpGet("{orderNumber}")]
    public async Task<IActionResult> Detail(
        string orderNumber, CancellationToken cancellationToken) =>
        HandleResult(await orders.GetMineByNumberAsync(orderNumber, cancellationToken));

    /// <summary>Stops the order, while it is still the customer's to stop.</summary>
    /// <remarks>
    /// Refused once the shop has started work. That boundary is narrower than
    /// what staff can cancel: a half-built wardrobe is a conversation about a
    /// deposit, not a button on a page.
    /// </remarks>
    [HttpPost("{orderNumber}/cancel")]
    public async Task<IActionResult> Cancel(
        string orderNumber,
        [FromBody] CancelOrderDto dto,
        CancellationToken cancellationToken) =>
        HandleResult(await orders.CancelMineAsync(orderNumber, dto, cancellationToken));

    /// <summary>
    /// A guest's order, by number and the phone number on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Anonymous by necessity — most orders here are placed without an account,
    /// and those customers still need to see where their sofa is.
    /// </para>
    /// <para>
    /// Two facts are required, not one. An order number alone is a series that
    /// can be walked; paired with the phone number it is not worth walking.
    /// Rate limited on the sensitive policy for the same reason: this is the
    /// one route on this controller that can be guessed at.
    /// </para>
    /// <para>
    /// A POST rather than a GET because the phone number is in the body. In a
    /// query string it would be in the browser history, in the proxy logs, and
    /// in the referrer header of anything the confirmation page links to.
    /// </para>
    /// </remarks>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [HttpPost("lookup")]
    public async Task<IActionResult> Lookup(
        [FromBody] GuestOrderLookupDto dto, CancellationToken cancellationToken) =>
        HandleResult(await orders.LookupGuestOrderAsync(dto, cancellationToken));
}
