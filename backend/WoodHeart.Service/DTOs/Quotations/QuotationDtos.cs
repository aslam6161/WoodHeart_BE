using System.ComponentModel.DataAnnotations;
using WoodHeart.Domain.Enums.Quotations;
using WoodHeart.Service.DTOs.Ordering;

namespace WoodHeart.Service.DTOs.Quotations;

/// <summary>A quotation as a screen renders it.</summary>
public class QuotationDto
{
    public long Id { get; init; }

    public string QuotationNumber { get; init; } = string.Empty;

    /// <summary>The consultation it came out of, when it came out of one.</summary>
    public string? BookingNumber { get; init; }

    public string ContactName { get; init; } = string.Empty;

    /// <summary>Masked for the customer's own view — <c>017****5678</c>.</summary>
    public string ContactPhone { get; init; } = string.Empty;

    public string? ContactEmail { get; init; }

    public DeliveryAddressDto? ShippingAddress { get; init; }

    public QuotationStatus Status { get; init; }

    public DateOnly ValidUntil { get; init; }

    /// <summary>True once the date has passed, whatever the status still says.</summary>
    public bool HasLapsed { get; init; }

    public DateTimeOffset? SentAt { get; init; }

    public DateTimeOffset? RespondedAt { get; init; }

    public string? DeclineReason { get; init; }

    public string? Notes { get; init; }

    /// <summary>Staff only. Absent from the customer's view entirely.</summary>
    public string? InternalNotes { get; init; }

    public decimal Subtotal { get; init; }

    public decimal DiscountTotal { get; init; }

    public decimal GoodsNet { get; init; }

    public decimal VatAmount { get; init; }

    public decimal VatRatePercent { get; init; }

    public bool PricesIncludeVat { get; init; }

    public decimal DeliveryFee { get; init; }

    public bool DeliveryOverridden { get; init; }

    /// <summary>Goods, VAT and delivery. Any payment charge is added at checkout.</summary>
    public decimal GrandTotal { get; init; }

    /// <summary>The order it became, once it became one.</summary>
    public string? OrderNumber { get; init; }

    public IReadOnlyList<QuotationLineDto> Lines { get; init; } = [];

    /// <summary>Whether the customer may still accept or decline it.</summary>
    public bool CanAnswer { get; init; }

    /// <summary>
    /// Every status legal from this one, straight from the API's own table.
    /// </summary>
    /// <remarks>
    /// Sent rather than reimplemented in Angular, as an order's and a booking's
    /// are. A second copy of the graph drifts, and the drift is a button that
    /// renders, is pressed, and comes back 409.
    /// </remarks>
    public IReadOnlyList<QuotationStatus> AllowedStatusTransitions { get; init; } = [];
}

public class QuotationLineDto
{
    public long Id { get; init; }

    /// <summary>Null for anything made to measure.</summary>
    public long? ProductVariantId { get; init; }

    public string Description { get; init; } = string.Empty;

    public string? VariantName { get; init; }

    public string? Sku { get; init; }

    public string? ImagePath { get; init; }

    public int Quantity { get; init; }

    public decimal UnitPrice { get; init; }

    public decimal LineTotal { get; init; }

    public int? LeadTimeDays { get; init; }

    /// <summary>Whether this comes off a shelf or is made.</summary>
    public bool HoldsStock { get; init; }
}

/// <summary>One row of the shop's quotation list.</summary>
public class QuotationListItemDto
{
    public long Id { get; init; }

    public string QuotationNumber { get; init; } = string.Empty;

    public string ContactName { get; init; } = string.Empty;

    /// <summary>Unmasked: the list is behind a staff policy and is what gets rung from.</summary>
    public string ContactPhone { get; init; } = string.Empty;

    public QuotationStatus Status { get; init; }

    public bool HasLapsed { get; init; }

    public DateOnly ValidUntil { get; init; }

    public decimal GrandTotal { get; init; }

    public int LineCount { get; init; }

    public string? BookingNumber { get; init; }

    public string? OrderNumber { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Creating or replacing a quotation. The lines come with it.</summary>
/// <remarks>
/// <b>Saved whole, like a consultant's week.</b> A quotation is one document —
/// the discount only makes sense beside the lines it comes off — and a partial
/// save is a quotation whose total disagrees with itself.
/// </remarks>
public class SaveQuotationDto
{
    /// <summary>The consultation this came out of. Optional.</summary>
    public long? BookingId { get; init; }

    [Required]
    [StringLength(120, MinimumLength = 2)]
    public string ContactName { get; init; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string ContactPhone { get; init; } = string.Empty;

    [EmailAddress]
    [StringLength(256)]
    public string? ContactEmail { get; init; }

    /// <summary>Needed before it can be sent, because delivery cannot be priced without it.</summary>
    public DeliveryAddressDto? ShippingAddress { get; init; }

    public DateOnly? ValidUntil { get; init; }

    /// <summary>Money off the goods, as a figure the designer typed.</summary>
    [Range(0, 99_999_999)]
    public decimal Discount { get; init; }

    /// <summary>Delivery priced by hand. Null lets the products decide.</summary>
    [Range(0, 9_999_999)]
    public decimal? DeliveryFeeOverride { get; init; }

    [StringLength(4000)]
    public string? Notes { get; init; }

    [StringLength(2000)]
    public string? InternalNotes { get; init; }

    public IReadOnlyList<SaveQuotationLineDto> Lines { get; init; } = [];
}

/// <summary>
/// One line. Either a catalogue variant or something made to measure.
/// </summary>
/// <remarks>
/// Naming a variant fills the description, the SKU and the photograph from the
/// catalogue and takes the price from there unless one is given — a designer
/// who quotes a bed at a negotiated price is doing something ordinary. Leaving
/// it null makes the line whatever the description says, which is how "wardrobe
/// to the alcove, 7ft" gets quoted at all.
/// </remarks>
public class SaveQuotationLineDto
{
    public long? ProductVariantId { get; init; }

    [StringLength(500)]
    public string? Description { get; init; }

    [Range(1, 9999)]
    public int Quantity { get; init; } = 1;

    /// <summary>Null takes the catalogue's price. Required without a variant.</summary>
    [Range(0, 99_999_999)]
    public decimal? UnitPrice { get; init; }

    [Range(0, 3650)]
    public int? LeadTimeDays { get; init; }
}

/// <summary>Moving a quotation along, from the shop's side.</summary>
public class ChangeQuotationStatusDto
{
    public QuotationStatus Status { get; init; }

    [StringLength(500)]
    public string? Reason { get; init; }
}

/// <summary>The customer answering one.</summary>
/// <remarks>
/// The phone is in the body rather than the query string for the same reason a
/// guest order lookup puts it there: a URL ends up in browser history, in a
/// proxy log and in a referrer header.
/// </remarks>
public class AnswerQuotationDto
{
    public string? Phone { get; init; }

    /// <summary>Why not, when they say why. Worth asking for.</summary>
    [StringLength(500)]
    public string? Reason { get; init; }
}

/// <summary>Turning an accepted quotation into an order.</summary>
public class ConvertQuotationDto
{
    /// <summary>Defaults to cash on delivery, which is how most of these are paid.</summary>
    [StringLength(32)]
    public string? PaymentMethodCode { get; init; }

    [StringLength(500)]
    public string? DeliveryNote { get; init; }
}

/// <summary>The shop's list filters.</summary>
public class QuotationQueryDto
{
    [StringLength(60)]
    public string? Term { get; init; }

    public QuotationStatus? Status { get; init; }

    public long? BookingId { get; init; }

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}

/// <summary>Limits shared between the attributes and the service.</summary>
public static class QuotationRules
{
    /// <summary>How long a quotation stands when nobody says. Timber prices move.</summary>
    public const int DefaultValidDays = 14;

    public const int DefaultPageSize = 20;

    public const int MaxPageSize = 100;

    public const int MaxLines = 100;
}
