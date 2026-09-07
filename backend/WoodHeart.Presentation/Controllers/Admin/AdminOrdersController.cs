using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoodHeart.Domain.Constants;
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
public class AdminOrdersController(IAdminOrderService orders) : BaseApiController
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

    /// <summary>The staff notepad. Never reaches a customer-facing DTO.</summary>
    [HttpPut("{orderNumber}/notes")]
    public async Task<IActionResult> UpdateNotes(
        string orderNumber,
        [FromBody] UpdateInternalNotesDto dto,
        CancellationToken cancellationToken) =>
        HandleResult(await orders.UpdateInternalNotesAsync(orderNumber, dto, cancellationToken));
}
