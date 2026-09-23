using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WoodHeart.Domain.Constants;
using WoodHeart.Service.DTOs.Quotations;
using WoodHeart.Service.Interfaces.Quotations;

namespace WoodHeart.Presentation.Controllers;

/// <summary>
/// The customer's side of a quotation: read it, take it, or turn it down.
/// </summary>
/// <remarks>
/// <para>
/// <c>[AllowAnonymous]</c> for the same reason the consultation endpoints are:
/// most people quoted for a flat have no account, and asking them to make one
/// before they can say yes to a price is how a sale is lost. A guest quotes
/// the number and the phone it was written for.
/// </para>
/// <para>
/// Answering is on the checkout rate-limiting policy rather than the public
/// one. It writes, it commits the shop to a price, and it is the step somebody
/// probing numbers would hammer.
/// </para>
/// </remarks>
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Public)]
[Route("api/quotations")]
public class QuotationsController(IQuotationService quotations) : BaseApiController
{
    /// <summary>
    /// One quotation, for whoever it was written for.
    /// </summary>
    /// <remarks>
    /// The phone is a query parameter here because reading is a GET and a GET
    /// has no body; answering below puts it in the body, where it belongs, for
    /// the write that matters.
    /// </remarks>
    [HttpGet("{quotationNumber}")]
    public async Task<IActionResult> Get(
        string quotationNumber,
        [FromQuery] string? phone,
        CancellationToken cancellationToken) =>
        HandleResult(await quotations.GetAsync(quotationNumber, phone, cancellationToken));

    /// <summary>The customer saying yes. The shop turns it into an order.</summary>
    [EnableRateLimiting(RateLimitPolicies.Checkout)]
    [HttpPost("{quotationNumber}/accept")]
    public async Task<IActionResult> Accept(
        string quotationNumber,
        [FromBody] AnswerQuotationDto dto,
        CancellationToken cancellationToken) =>
        HandleResult(await quotations.AcceptAsync(quotationNumber, dto?.Phone, cancellationToken));

    /// <summary>The customer saying no, and ideally why.</summary>
    [EnableRateLimiting(RateLimitPolicies.Checkout)]
    [HttpPost("{quotationNumber}/decline")]
    public async Task<IActionResult> Decline(
        string quotationNumber,
        [FromBody] AnswerQuotationDto dto,
        CancellationToken cancellationToken) =>
        HandleResult(
            await quotations.DeclineAsync(
                quotationNumber, dto?.Phone, dto?.Reason, cancellationToken));

    /// <summary>A signed-in customer's own quotations.</summary>
    [Authorize(Policy = Policies.RequireCustomer)]
    [HttpGet("my-quotations")]
    public async Task<IActionResult> Mine(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = QuotationRules.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        HandleResult(await quotations.GetMineAsync(page, pageSize, cancellationToken));
}
