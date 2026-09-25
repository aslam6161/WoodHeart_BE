using System.ComponentModel.DataAnnotations;
using WoodHeart.Domain.Enums.Ordering;

namespace WoodHeart.Service.DTOs.Ordering;

/// <summary>
/// An order as the staff order board shows it.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not <see cref="OrderSummaryDto"/> with extra fields on it. The
/// customer's row masks the phone number and hides the internal notes; this one
/// must not, because the first thing anybody does with an order is ring the
/// customer about it. Two audiences, two shapes — and no risk that adding a
/// field for staff quietly puts it on a customer's page.
/// </para>
/// </remarks>
public class AdminOrderSummaryDto
{
    public long Id { get; init; }

    public string OrderNumber { get; init; } = string.Empty;

    public DateTimeOffset PlacedAt { get; init; }

    public string ContactName { get; init; } = string.Empty;

    /// <summary>In full. Staff need to be able to dial it.</summary>
    public string ContactPhone { get; init; } = string.Empty;

    public OrderStatus Status { get; init; }

    public PaymentStatus PaymentStatus { get; init; }

    public FulfilmentStatus FulfilmentStatus { get; init; }

    public string PaymentMethodCode { get; init; } = string.Empty;

    public DeliveryZone DeliveryZone { get; init; }

    /// <summary>Enough of the address to tell two Rakibs apart in a list.</summary>
    public string ShippingArea { get; init; } = string.Empty;

    public decimal GrandTotal { get; init; }

    public decimal DeliveryFee { get; init; }

    /// <summary>Flagged in the list, because an overridden charge invites a second look.</summary>
    public bool DeliveryOverridden { get; init; }

    public int ItemCount { get; init; }

    /// <summary>False when the order was placed without an account, which most are.</summary>
    public bool HasAccount { get; init; }
}

/// <summary>
/// The whole order as staff see it: unmasked, annotated, and with the moves
/// that are legal from here.
/// </summary>
public class AdminOrderDetailDto
{
    public long Id { get; init; }

    public string OrderNumber { get; init; } = string.Empty;

    public DateTimeOffset PlacedAt { get; init; }

    public OrderStatus Status { get; init; }

    public PaymentStatus PaymentStatus { get; init; }

    public FulfilmentStatus FulfilmentStatus { get; init; }

    public string PaymentMethodCode { get; init; } = string.Empty;

    /// <summary>
    /// Every order status legal from the current one, straight from
    /// <c>OrderStatusMachine</c>.
    /// </summary>
    /// <remarks>
    /// Sent rather than reimplemented in Angular. A second copy of the graph in
    /// TypeScript would drift, and the drift shows up as a button that renders,
    /// is pressed, and returns a 409.
    /// </remarks>
    public IReadOnlyList<OrderStatus> AllowedStatusTransitions { get; init; } = [];

    public IReadOnlyList<PaymentStatus> AllowedPaymentTransitions { get; init; } = [];

    /// <summary>
    /// Every sum that changed hands over this order, in the order it moved.
    /// </summary>
    /// <remarks>
    /// The payment status says where the money stands; this says what happened
    /// to get there. Staff asking "when did we take that, and who from" have
    /// nowhere else to look, and on a shop whose takings are mostly cash the
    /// alternative is somebody's memory.
    /// </remarks>
    public IReadOnlyList<OrderPaymentDto> Payments { get; init; } = [];

    /// <summary>Taken less given back.</summary>
    public decimal AmountPaid { get; init; }

    /// <summary>Still owed. Never negative — an overpayment is not a debt.</summary>
    public decimal AmountOutstanding { get; init; }

    /// <summary>Whether the delivery charge may still be edited on this order.</summary>
    public bool CanEditDeliveryFee { get; init; }

    public long? CustomerId { get; init; }

    public string ContactName { get; init; } = string.Empty;

    /// <summary>In full — this is the staff view.</summary>
    public string ContactPhone { get; init; } = string.Empty;

    public string? ContactEmail { get; init; }

    public DeliveryAddressDto ShippingAddress { get; init; } = new();

    /// <summary>The address on one line, for a label or a rider's message.</summary>
    public string ShippingAddressLine { get; init; } = string.Empty;

    public DeliveryZone DeliveryZone { get; init; }

    public string? DeliveryNote { get; init; }

    /// <summary>Staff-only, and never on any customer-facing DTO.</summary>
    public string? InternalNotes { get; init; }

    public string Currency { get; init; } = string.Empty;

    public IReadOnlyList<AdminOrderLineDto> Lines { get; init; } = [];

    public OrderTotalsDto Totals { get; init; } = new();

    /// <summary>
    /// What the products' own charges add up to, for comparison with what was
    /// actually charged.
    /// </summary>
    /// <remarks>
    /// The pair of this and <c>Totals.DeliveryFee</c> is what makes an override
    /// auditable: "the rate card said 4,500৳, we charged 1,600৳, and the note
    /// says they went in one van."
    /// </remarks>
    public decimal DeliveryChargeFromLines { get; init; }

    public IReadOnlyList<OrderTimelineEntryDto> Timeline { get; init; } = [];

    /// <summary>The basket this came from, kept as evidence.</summary>
    public long? CartId { get; init; }
}

/// <summary>An order line, with the delivery it contributed.</summary>
public class AdminOrderLineDto
{
    public long Id { get; init; }

    /// <summary>Null for a line made to measure rather than picked off a shelf.</summary>
    public long? VariantId { get; init; }

    public long? ProductId { get; init; }

    public string ProductName { get; init; } = string.Empty;

    public string ProductSlug { get; init; } = string.Empty;

    public string Sku { get; init; } = string.Empty;

    public string VariantName { get; init; } = string.Empty;

    public string? ImagePath { get; init; }

    public int Quantity { get; init; }

    public decimal UnitPrice { get; init; }

    public decimal DiscountAmount { get; init; }

    public decimal LineTotal { get; init; }

    /// <summary>
    /// What this line put into the delivery charge at placement.
    /// </summary>
    /// <remarks>
    /// The reason it is stored on the line rather than recomputed: the
    /// products' charges will have been edited since, and recomputing would
    /// answer a question about last month with this month's rate card.
    /// </remarks>
    public decimal DeliveryChargeApplied { get; init; }

    public int? LeadTimeDays { get; init; }
}

/// <summary>How many orders sit in each status, for the board's tabs.</summary>
public class OrderStatusCountDto
{
    public OrderStatus Status { get; init; }

    public int Count { get; init; }
}

/// <summary>Moving an order along the work axis.</summary>
public class ChangeOrderStatusDto
{
    [Required]
    public OrderStatus Status { get; init; }

    /// <summary>
    /// Why. Required when cancelling, optional otherwise.
    /// </summary>
    /// <remarks>
    /// "Cancelled" with no reason is the entry somebody will be asked about in
    /// six months, and a blank is not an answer. The other moves are ordinary
    /// progress and explain themselves.
    /// </remarks>
    [MaxLength(500)]
    public string? Note { get; init; }
}

/// <summary>Recording where the money got to.</summary>
public class RecordPaymentDto
{
    [Required]
    public PaymentStatus Status { get; init; }

    /// <summary>
    /// How much changed hands. Left out, the obvious figure is used.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Optional because the common case has one right answer and making
    /// somebody type it invites a typo: marking a cash order Paid means the
    /// whole balance, and Refunded means everything taken so far. An advance
    /// is the case that needs saying, which is the case that never could be.
    /// </para>
    /// <para>
    /// More than the balance is refused rather than recorded. A shop that can
    /// book a payment larger than the order can book one twice as large, and
    /// the first anybody knows of it is the month-end figure.
    /// </para>
    /// </remarks>
    [Range(0.01, 99_999_999)]
    public decimal? Amount { get; init; }

    /// <summary>A bKash transaction id, a bank slip number, the rider's name.</summary>
    [MaxLength(200)]
    public string? Reference { get; init; }

    /// <summary>A receipt number, a bKash transaction id, "cash to Rakib".</summary>
    [MaxLength(500)]
    public string? Note { get; init; }
}

/// <summary>One sum that changed hands, for the order screen.</summary>
public class OrderPaymentDto
{
    public PaymentDirection Direction { get; init; }

    public decimal Amount { get; init; }

    public string MethodCode { get; init; } = string.Empty;

    /// <summary>A bKash transaction id, a bank slip number, the rider's name.</summary>
    public string? Reference { get; init; }

    public string ActorName { get; init; } = string.Empty;

    public string? Note { get; init; }

    public DateTimeOffset OccurredAt { get; init; }
}

/// <summary>Where the goods got to, which is not where the order got to.</summary>
public class RecordFulfilmentDto
{
    [Required]
    public FulfilmentStatus Status { get; init; }

    [MaxLength(500)]
    public string? Note { get; init; }
}

/// <summary>
/// Staff correcting the delivery charge by hand.
/// </summary>
/// <remarks>
/// The whole reason the field exists: a bed and its two bedside tables price at
/// three separate deliveries and go out on one van with the same two men. The
/// rate card cannot say that; the person looking at the order can.
/// </remarks>
public class OverrideDeliveryFeeDto
{
    /// <summary>The figure to charge. Zero is allowed — free delivery is a real decision.</summary>
    [Required]
    [Range(0, 1_000_000)]
    public decimal DeliveryFee { get; init; }

    /// <summary>
    /// Required, not optional.
    /// </summary>
    /// <remarks>
    /// An unexplained change to what a customer is charged is indistinguishable
    /// from a mistake when somebody reads it back later, and this is the one
    /// field on the order that lets a member of staff move money.
    /// </remarks>
    [Required]
    [MinLength(3)]
    [MaxLength(300)]
    public string Reason { get; init; } = string.Empty;
}

/// <summary>The staff notepad on an order.</summary>
public class UpdateInternalNotesDto
{
    [MaxLength(2000)]
    public string? InternalNotes { get; init; }
}
