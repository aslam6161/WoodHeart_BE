using System.ComponentModel.DataAnnotations;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.Enums.Promotions;

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

/// <summary>Put a coupon code on the basket.</summary>
/// <remarks>
/// Refused outright when it does not apply, with the reason: a code sitting on
/// a basket doing nothing is a customer who thinks they have a discount and
/// finds out at the till that they have not.
/// </remarks>
public class ApplyCouponDto
{
    [Required]
    [StringLength(40, MinimumLength = 3)]
    public string Code { get; init; } = string.Empty;
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

    /// <summary>
    /// What came off, named. Automatic promotions and coupons alike.
    /// </summary>
    /// <remarks>
    /// Named rather than summed into one figure, because "−2,000৳" on its own
    /// reads as an error to anyone who was not expecting it, and because a
    /// customer who can see "September sale −2,000৳" knows not to go hunting
    /// for a better code.
    /// </remarks>
    public IReadOnlyList<CartDiscountDto> Discounts { get; init; } = [];

    /// <summary>The codes on this basket, and whether each one is actually doing anything.</summary>
    public IReadOnlyList<CartCouponDto> Coupons { get; init; } = [];

    /// <summary>True when a discount takes the delivery charge off.</summary>
    public bool FreeShipping { get; init; }
}

/// <summary>One discount that applied to the basket.</summary>
public class CartDiscountDto
{
    public long DiscountId { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>Null for an automatic promotion — nothing was typed.</summary>
    public string? Code { get; init; }

    public DiscountType Type { get; init; }

    /// <summary>What it took off. For free shipping, the delivery charge waived.</summary>
    public decimal Amount { get; init; }
}

/// <summary>A coupon code on the basket.</summary>
/// <remarks>
/// A code can stop applying after it was accepted — the customer removes the
/// sofa that qualified them, or the limit is reached while they hesitate — so
/// the basket carries both the code and its current standing rather than
/// silently dropping it.
/// </remarks>
public class CartCouponDto
{
    public string Code { get; init; } = string.Empty;

    public bool IsApplied { get; init; }

    /// <summary>A <c>PromotionErrors</c> code when it is not applying. Null when it is.</summary>
    public string? Reason { get; init; }
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

    /// <summary>
    /// The product is still sold, but there are none to sell. Distinguished
    /// from a withdrawn product because the advice differs: "check back" as
    /// opposed to "remove it".
    /// </summary>
    public bool IsSoldOut { get; init; }

    /// <summary>The count, when it is small enough to matter. Null otherwise.</summary>
    public int? AvailableQuantity { get; init; }

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
