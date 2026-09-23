using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoodHeart.Domain.Constants;
using WoodHeart.Service.DTOs.Quotations;
using WoodHeart.Service.Interfaces.Quotations;

namespace WoodHeart.Presentation.Controllers.Admin;

/// <summary>
/// Quotations: what the shop offered to do, and what became of the offer.
/// </summary>
/// <remarks>
/// Reading is for all staff — whoever answers the telephone is asked "what did
/// you quote me". Writing one is admin or manager: a quotation is a price the
/// shop is held to, and it can be accepted the moment it is sent.
/// </remarks>
[Authorize(Policy = Policies.RequireStaff)]
[Route("api/admin/quotations")]
public class AdminQuotationsController(IQuotationService quotations) : BaseApiController
{
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] QuotationQueryDto query, CancellationToken cancellationToken) =>
        HandleResult(await quotations.SearchAsync(query, cancellationToken));

    [HttpGet("{quotationNumber}")]
    public async Task<IActionResult> Get(
        string quotationNumber, CancellationToken cancellationToken) =>
        HandleResult(await quotations.GetForStaffAsync(quotationNumber, cancellationToken));

    /// <summary>
    /// Writes a quotation, lines and all.
    /// </summary>
    /// <remarks>
    /// Whole, like a consultant's week: the discount only means something
    /// beside the lines it comes off, and a partial save is a quotation whose
    /// total disagrees with itself.
    /// </remarks>
    [Authorize(Policy = Policies.RequireAdminOrManager)]
    [HttpPost]
    public async Task<IActionResult> Create(
        SaveQuotationDto dto, CancellationToken cancellationToken) =>
        HandleResult(await quotations.SaveAsync(null, dto, cancellationToken));

    [Authorize(Policy = Policies.RequireAdminOrManager)]
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(
        long id, SaveQuotationDto dto, CancellationToken cancellationToken) =>
        HandleResult(await quotations.SaveAsync(id, dto, cancellationToken));

    /// <summary>Send it, pull it back, or write it off.</summary>
    [Authorize(Policy = Policies.RequireAdminOrManager)]
    [HttpPut("{quotationNumber}/status")]
    public async Task<IActionResult> SetStatus(
        string quotationNumber,
        ChangeQuotationStatusDto dto,
        CancellationToken cancellationToken) =>
        HandleResult(
            await quotations.SetStatusAsync(
                quotationNumber, dto.Status, dto.Reason, cancellationToken));

    /// <summary>The ways this quotation may be paid for, with any charge.</summary>
    /// <remarks>
    /// Read-only, so every staff role may see it; converting below is
    /// narrower. The methods are worked out against the quotation's own total
    /// rather than a basket, because whoever is converting one does not have
    /// a basket.
    /// </remarks>
    [HttpGet("{quotationNumber}/payment-methods")]
    public async Task<IActionResult> PaymentMethods(
        string quotationNumber, CancellationToken cancellationToken) =>
        HandleResult(await quotations.GetPaymentMethodsAsync(quotationNumber, cancellationToken));

    /// <summary>
    /// Turns an accepted quotation into an order.
    /// </summary>
    /// <remarks>
    /// The one endpoint Phase 4 exists for. It does not re-price: the customer
    /// agreed to a figure and the order carries that figure, plus whatever the
    /// chosen way of paying costs.
    /// </remarks>
    [Authorize(Policy = Policies.RequireAdminOrManager)]
    [HttpPost("{quotationNumber}/convert")]
    public async Task<IActionResult> Convert(
        string quotationNumber,
        ConvertQuotationDto dto,
        CancellationToken cancellationToken) =>
        HandleResult(await quotations.ConvertAsync(quotationNumber, dto, cancellationToken));
}
