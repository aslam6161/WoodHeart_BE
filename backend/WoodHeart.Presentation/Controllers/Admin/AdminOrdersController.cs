using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoodHeart.Domain.Constants;
using WoodHeart.Repository;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Service.DTOs.Ordering;
using WoodHeart.Service.Interfaces.Ordering;

namespace WoodHeart.Presentation.Controllers.Admin;

/// <summary>
/// The order board: everything staff do to an order after it is placed.
/// </summary>
/// <remarks>
/// <para>
/// <c>RequireStaff</c> rather than <c>RequireAdminOrManager</c>, because
/// working orders through is the job — the person packing a wardrobe needs to
/// mark it ready to ship, and an order board only a manager can touch is an
/// order board nobody uses. The two operations that move money are narrower:
/// see the attributes below.
/// </para>
/// <para>
/// Orders are addressed by their number here as well as on the storefront. It
/// is what staff read off the customer's SMS while they are on the phone, and
/// having one address for an order across both panels means a support call does
/// not need a translation step.
/// </para>
/// </remarks>
[Authorize(Policy = Policies.RequireStaff)]
[Route("api/admin/orders")]
public class AdminOrdersController(
    IAdminOrderService orders,
    IInvoiceService invoices) : BaseApiController
{
    /// <summary>The board, filtered by status and searched by number, phone or name.</summary>
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] OrderStatus? status = null,
        [FromQuery] string? term = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = OrderRules.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        HandleResult(await orders.SearchAsync(status, term, page, pageSize, cancellationToken));

    /// <summary>A count per status, for the tabs above the board.</summary>
    [HttpGet("status-counts")]
    public async Task<IActionResult> StatusCounts(CancellationToken cancellationToken) =>
        HandleResult(await orders.GetStatusCountsAsync(cancellationToken));

    /// <summary>One order in full — unmasked, with its notes and its legal moves.</summary>
    [HttpGet("{orderNumber}")]
    public async Task<IActionResult> Detail(
        string orderNumber, CancellationToken cancellationToken) =>
        HandleResult(await orders.GetAsync(orderNumber, cancellationToken));

    /// <summary>Moves the order along the work axis.</summary>
    [HttpPost("{orderNumber}/status")]
    public async Task<IActionResult> ChangeStatus(
        string orderNumber,
        [FromBody] ChangeOrderStatusDto dto,
        CancellationToken cancellationToken) =>
        HandleResult(await orders.ChangeStatusAsync(orderNumber, dto, cancellationToken));

    /// <summary>Records where the goods got to, for a part-shipped order.</summary>
    [HttpPost("{orderNumber}/fulfilment")]
    public async Task<IActionResult> RecordFulfilment(
        string orderNumber,
        [FromBody] RecordFulfilmentDto dto,
        CancellationToken cancellationToken) =>
        HandleResult(await orders.RecordFulfilmentAsync(orderNumber, dto, cancellationToken));

    /// <summary>
    /// Records where the money got to.
    /// </summary>
    /// <remarks>
    /// Narrower than the rest of the board. Marking an order paid is a claim
    /// about cash that has or has not arrived, and it is the entry an audit
    /// starts from — so it belongs to whoever is accountable for the till
    /// rather than to everyone who can pack a box.
    /// </remarks>
    [Authorize(Policy = Policies.RequireAdminOrManager)]
    [HttpPost("{orderNumber}/payment")]
    public async Task<IActionResult> RecordPayment(
        string orderNumber,
        [FromBody] RecordPaymentDto dto,
        CancellationToken cancellationToken) =>
        HandleResult(await orders.RecordPaymentAsync(orderNumber, dto, cancellationToken));

    /// <summary>
    /// Corrects the delivery charge by hand, and the total with it.
    /// </summary>
    /// <remarks>
    /// Also restricted, for the same reason: this is the one place where one
    /// person can change what a customer is charged. The service requires a
    /// reason and writes it to the timeline, so the change is attributable
    /// whatever else happens.
    /// </remarks>
    [Authorize(Policy = Policies.RequireAdminOrManager)]
    [HttpPost("{orderNumber}/delivery-fee")]
    public async Task<IActionResult> OverrideDeliveryFee(
        string orderNumber,
        [FromBody] OverrideDeliveryFeeDto dto,
        CancellationToken cancellationToken) =>
        HandleResult(await orders.OverrideDeliveryFeeAsync(orderNumber, dto, cancellationToken));

    /// <summary>
    /// The invoice, as a PDF.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one endpoint on this controller that does not return a
    /// <c>GeneralResponse</c>, because the caller is a browser being handed a
    /// file rather than Angular reading a payload. A failure still does — the
    /// service returns the same shape as everything else, and only the success
    /// path becomes bytes.
    /// </para>
    /// <para>
    /// <c>inline</c> rather than <c>attachment</c>: staff open the invoice to
    /// check it and then print it, and a download that lands in a folder is one
    /// more step and one more stale copy on somebody's desktop.
    /// </para>
    /// <para>
    /// <b>No <c>[Produces]</c> attribute, deliberately.</b> It sets the content
    /// types the result may be formatted as, and the success path here is a
    /// <c>FileContentResult</c> that writes its own. Naming the PDF type there
    /// leaves the framework with no formatter for the <c>GeneralResponse</c> on
    /// the failure path, so a missing order number answers 406 Not Acceptable
    /// with an empty body instead of the 404 the client knows how to read. The
    /// response types below document the same thing without changing
    /// negotiation.
    /// </para>
    /// </remarks>
    [HttpGet("{orderNumber}/invoice")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, InvoiceFile.Pdf)]
    [ProducesResponseType(typeof(GeneralResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Invoice(
        string orderNumber, CancellationToken cancellationToken)
    {
        var result = await invoices.RenderAsync(orderNumber, cancellationToken);

        if (!result.IsSuccess || result.Data is null)
        {
            return HandleResult(result);
        }

        Response.Headers.ContentDisposition =
            $"inline; filename=\"{result.Data.FileName}\"";

        return File(result.Data.Content, InvoiceFile.Pdf);
    }

    /// <summary>The staff notepad. Never reaches a customer-facing DTO.</summary>
    [HttpPut("{orderNumber}/notes")]
    public async Task<IActionResult> UpdateNotes(
        string orderNumber,
        [FromBody] UpdateInternalNotesDto dto,
        CancellationToken cancellationToken) =>
        HandleResult(await orders.UpdateInternalNotesAsync(orderNumber, dto, cancellationToken));
}
