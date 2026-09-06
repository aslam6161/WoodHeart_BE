using System.ComponentModel.DataAnnotations;
using WoodHeart.Domain.Enums.Ordering;

namespace WoodHeart.Service.DTOs.Ordering;

/// <summary>An order as a customer's list of orders shows it.</summary>
public class OrderSummaryDto
{
    public long Id { get; init; }

    public string OrderNumber { get; init; } = string.Empty;

    public OrderStatus Status { get; init; }

    public PaymentStatus PaymentStatus { get; init; }

    public DateTimeOffset PlacedAt { get; init; }

    public decimal GrandTotal { get; init; }

    public int ItemCount { get; init; }

    /// <summary>The first line's photo, so a list row has something to look at.</summary>
    public string? PreviewImagePath { get; init; }

    /// <summary>"Segun Bed and 2 more" — what the row says it is.</summary>
    public string PreviewTitle { get; init; } = string.Empty;

    /// <summary>Whether the customer can still stop it themselves.</summary>
    public bool CanCancel { get; init; }
}

/// <summary>The whole order, as its own page shows it.</summary>
public class OrderDetailDto
{
    public long Id { get; init; }

    public string OrderNumber { get; init; } = string.Empty;

    public OrderStatus Status { get; init; }

    public PaymentStatus PaymentStatus { get; init; }

    public FulfilmentStatus FulfilmentStatus { get; init; }

    public string PaymentMethodCode { get; init; } = string.Empty;

    public DateTimeOffset PlacedAt { get; init; }

    public string ContactName { get; init; } = string.Empty;

    /// <summary>Masked — <c>017****5678</c>.</summary>
    /// <remarks>
    /// The full number is on the order and staff screens need it; a customer's
    /// own confirmation page does not, and a page that can be screenshotted and
    /// shared should not carry one more identifier than it has to.
    /// </remarks>
    public string ContactPhone { get; init; } = string.Empty;

    public string? ContactEmail { get; init; }

    public DeliveryAddressDto ShippingAddress { get; init; } = new();

    public DeliveryZone DeliveryZone { get; init; }

    public string? DeliveryNote { get; init; }

    public string Currency { get; init; } = string.Empty;

    public IReadOnlyList<OrderLineDto> Lines { get; init; } = [];

    public OrderTotalsDto Totals { get; init; } = new();

    public IReadOnlyList<OrderTimelineEntryDto> Timeline { get; init; } = [];

    public bool CanCancel { get; init; }
}

public class OrderLineDto
{
    public long Id { get; init; }

    public long VariantId { get; init; }

    public long ProductId { get; init; }

    public string ProductName { get; init; } = string.Empty;

    public string ProductSlug { get; init; } = string.Empty;

    public string Sku { get; init; } = string.Empty;

    public string VariantName { get; init; } = string.Empty;

    public string? ImagePath { get; init; }

    public int Quantity { get; init; }

    public decimal UnitPrice { get; init; }

    public decimal DiscountAmount { get; init; }

    public decimal LineTotal { get; init; }

    public int? LeadTimeDays { get; init; }
}

/// <summary>The bill, exactly as it was charged.</summary>
public class OrderTotalsDto
{
    public decimal Subtotal { get; init; }

    public decimal DiscountTotal { get; init; }

    public decimal GoodsNet { get; init; }

    public decimal VatAmount { get; init; }

    /// <summary>The rate that applied, so an old order shows its own VAT rate.</summary>
    public decimal VatRatePercent { get; init; }

    public bool PricesIncludeVat { get; init; }

    public decimal DeliveryFee { get; init; }

    public decimal PaymentSurcharge { get; init; }

    public decimal GrandTotal { get; init; }

    public bool DeliveryWaived { get; init; }

    public bool DeliveryOverridden { get; init; }
}

public class OrderTimelineEntryDto
{
    public OrderStatus? FromStatus { get; init; }

    public OrderStatus ToStatus { get; init; }

    public string ActorName { get; init; } = string.Empty;

    public string? Note { get; init; }

    public DateTimeOffset OccurredAt { get; init; }
}

/// <summary>
/// How a guest asks for their own order back.
/// </summary>
/// <remarks>
/// <para>
/// Two facts, both of which the customer has and neither of which is guessable
/// in bulk: the order number from their confirmation, and the phone number they
/// gave. Order number alone would let anyone walk the series; this endpoint is
/// also rate limited on the tight policy for the same reason.
/// </para>
/// <para>
/// A signed-in customer never uses this — their own orders are found from their
/// identity, with no secret to type.
/// </para>
/// </remarks>
public class GuestOrderLookupDto
{
    [Required]
    [MaxLength(24)]
    public string OrderNumber { get; init; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string ContactPhone { get; init; } = string.Empty;
}

/// <summary>Why the customer is stopping the order.</summary>
public class CancelOrderDto
{
    /// <summary>
    /// Optional, and worth asking for.
    /// </summary>
    /// <remarks>
    /// "Found it cheaper elsewhere" and "ordered the wrong size" lead to
    /// different fixes, and neither is discoverable from a cancellation count.
    /// </remarks>
    [MaxLength(500)]
    public string? Reason { get; init; }
}

/// <summary>One page of results, with enough to draw a pager.</summary>
public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];

    public int Total { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }
}

/// <summary>Paging limits, shared between the DTO attributes and the services.</summary>
public static class OrderRules
{
    public const int DefaultPageSize = 20;

    public const int MaxPageSize = 100;
}
