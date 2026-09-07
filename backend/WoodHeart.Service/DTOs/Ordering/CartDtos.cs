using System.ComponentModel.DataAnnotations;
using WoodHeart.Domain.Enums.Ordering;

namespace WoodHeart.Service.DTOs.Ordering;

/// <summary>Put a variant in the basket, or add to what is already there.</summary>
public class AddToCartDto
{
    [Range(1, long.MaxValue)]
    public long VariantId { get; init; }

    /// <summary>
    /// Capped at 99 per line.
    /// </summary>
    /// <remarks>
    /// Not an arbitrary round number: it is the point past which a request is
    /// either a mistyped quantity or somebody probing. A genuine bulk order for
    /// a hotel goes through the consultation flow, where somebody quotes it.
    /// </remarks>
    [Range(CartRules.MinQuantity, CartRules.MaxQuantityPerLine)]
    public int Quantity { get; init; } = 1;
}

/// <summary>Change one line's quantity. Zero removes the line.</summary>
public class UpdateCartLineDto
{
    [Range(0, CartRules.MaxQuantityPerLine)]
    public int Quantity { get; init; }
}

/// <summary>Tell the cart where it is going, so delivery can be priced.</summary>
public class SetDeliveryZoneDto
{
    public DeliveryZone Zone { get; init; }
}

/// <summary>Limits shared between the DTO attributes and the service.</summary>
/// <remarks>
/// In one place because the attribute and the service check must agree. When
/// they drift, one of them becomes dead code and nobody notices which.
/// </remarks>
public static class CartRules
{
    public const int MinQuantity = 1;

    public const int MaxQuantityPerLine = 99;

    /// <summary>
    /// How long an untouched cart stays live, in days.
    /// </summary>
    /// <remarks>
    /// Thirty days is long for a basket and deliberately so: furniture is a
    /// considered purchase, and someone comparing a wardrobe against two other
    /// shops over a fortnight should not lose their basket for taking the time.
    /// </remarks>
    public const int LifetimeDays = 30;
}

/// <summary>The basket as the storefront renders it.</summary>
public class CartDto
{
    public long Id { get; init; }

    public string Currency { get; init; } = string.Empty;

    public DeliveryZone? DeliveryZone { get; init; }

    public IReadOnlyList<CartLineDto> Lines { get; init; } = [];

    public CartTotalsDto Totals { get; init; } = new();

    /// <summary>
    /// True when at least one line's price has moved since it was added.
    /// </summary>
    /// <remarks>
    /// Surfaced at the top of the cart so the page can say it once, rather than
    /// leaving the customer to spot a changed number themselves and wonder
    /// whether they misremembered.
    /// </remarks>
    public bool HasPriceChanges { get; init; }

    /// <summary>
    /// True when something in the basket can no longer be bought — withdrawn
    /// from sale, or its variant deactivated.
    /// </summary>
    public bool HasUnavailableLines { get; init; }
}

public class CartLineDto
{
    public long Id { get; init; }

    public long VariantId { get; init; }

    public long ProductId { get; init; }

    public string ProductNameEn { get; init; } = string.Empty;

    public string? ProductNameBn { get; init; }

    public string ProductSlug { get; init; } = string.Empty;

    public string Sku { get; init; } = string.Empty;

    /// <summary>"Segun · 6ft · Matte" — what distinguishes this from its siblings.</summary>
    public string VariantName { get; init; } = string.Empty;

    public string? PrimaryImagePath { get; init; }

    public int Quantity { get; init; }

    /// <summary>The live price, which is what will be charged.</summary>
    public decimal UnitPrice { get; init; }

    /// <summary>What it cost when it went into the basket.</summary>
    public decimal UnitPriceAtAdd { get; init; }

    /// <summary>Set when the two differ, so the page can say so plainly.</summary>
    public bool PriceChanged { get; init; }

    public decimal LineTotal { get; init; }

    /// <summary>
    /// False when the product has been withdrawn or the variant switched off.
    /// The line stays visible — silently dropping something a customer chose is
    /// worse than showing it greyed out with a reason.
    /// </summary>
    public bool IsAvailable { get; init; }

    /// <summary>Working days to build, for made-to-order items. Null for stocked ones.</summary>
    public int? LeadTimeDays { get; init; }
}

/// <summary>
/// The bill. Mirrors <c>CartTotals</c> in the domain, flattened to decimals for
/// the wire.
/// </summary>
public class CartTotalsDto
{
    public decimal Subtotal { get; init; }

    public decimal DiscountTotal { get; init; }

    /// <summary>Goods excluding VAT.</summary>
    public decimal GoodsNet { get; init; }

    public decimal VatAmount { get; init; }

    public decimal DeliveryFee { get; init; }

    public decimal GrandTotal { get; init; }

    /// <summary>True when the free-delivery threshold waived the charge.</summary>
    public bool DeliveryWaived { get; init; }

    /// <summary>
    /// True when staff set the delivery charge by hand.
    /// </summary>
    /// <remarks>
    /// Shown to the customer, because otherwise the delivery line does not
    /// follow from the items and it looks like a mistake. "Delivery adjusted by
    /// WoodHeart" reads as attention; an unexplained number reads as an error.
    /// </remarks>
    public bool DeliveryOverridden { get; init; }

    /// <summary>
    /// Null once a zone is chosen. Until then the cart says "calculated at
    /// checkout" rather than quoting a Dhaka price to a Sylhet customer.
    /// </summary>
    public bool DeliveryPending { get; init; }

    public int ItemCount { get; init; }

    /// <summary>
    /// Whether the prices above already contain VAT, so the page can label the
    /// VAT line "included" rather than implying it was added on.
    /// </summary>
    public bool PricesIncludeVat { get; init; }
}
