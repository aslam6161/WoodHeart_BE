using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Enums.Catalog;
using WoodHeart.Domain.Enums.Promotions;
using WoodHeart.Domain.Pricing;
using WoodHeart.Domain.Promotions;

using WoodHeart.Service.Mapping.Catalog;

namespace WoodHeart.Service.Mapping.Ordering;

/// <summary>
/// Cart entities to the shapes the storefront renders.
/// </summary>
/// <remarks>
/// Hand-written, matching the rest of the codebase: a mapping library would
/// hide exactly the decisions worth seeing here — that an unavailable line is
/// still shown, and that the live price rather than the stored one is what
/// appears against it.
/// </remarks>
public static class CartMapper
{
    public static DTOs.Ordering.CartDto ToDto(
        Cart cart,
        CartTotals totals,
        PricingContext context,
        IReadOnlyList<AppliedDiscount> applied,
        IReadOnlyList<RejectedCoupon> rejected)
    {
        var lines = cart.Lines.Select(ToLineDto).ToList();

        return new DTOs.Ordering.CartDto
        {
            Id = cart.Id,
            Currency = cart.Currency,
            DeliveryZone = cart.DeliveryZone,
            Lines = lines,
            Totals = ToTotalsDto(totals, context),
            HasPriceChanges = lines.Any(l => l.PriceChanged),
            HasUnavailableLines = lines.Any(l => !l.IsAvailable),

            // Only the ones that gave something. A discount recorded at zero —
            // a free-shipping coupon on an order whose delivery staff have
            // already set by hand — would read on the basket as an offer that
            // is not working.
            Discounts =
            [
                .. applied
                    .Where(discount => discount.Amount.IsPositive)
                    .Select(discount => new DTOs.Ordering.CartDiscountDto
                    {
                        DiscountId = discount.DiscountId,
                        Name = discount.Name,
                        Code = discount.Code,
                        Type = discount.Type,
                        Amount = discount.Amount.Amount
                    })
            ],

            // Every code the customer has typed, applying or not. A code that
            // has stopped applying is removed from the total and kept on the
            // basket with its reason: silently dropping it would leave them
            // hunting for a code they had already found.
            Coupons =
            [
                .. cart.Coupons.Select(coupon => new DTOs.Ordering.CartCouponDto
                {
                    Code = coupon.Code,
                    IsApplied = applied.Any(discount =>
                        string.Equals(discount.Code, coupon.Code, StringComparison.Ordinal)),
                    Reason = rejected
                        .FirstOrDefault(refusal =>
                            string.Equals(refusal.Code, coupon.Code, StringComparison.Ordinal))
                        ?.Reason
                })
            ],

            FreeShipping = applied.Any(discount => discount.Type == DiscountType.FreeShipping)
        };
    }

    /// <summary>A priced, empty basket — what a first-time visitor sees.</summary>
    public static DTOs.Ordering.CartDto Empty(CartTotals totals, PricingContext context) =>
        new()
        {
            Id = 0,
            Currency = Domain.ValueObjects.Money.Bdt,
            Lines = [],
            Totals = ToTotalsDto(totals, context)
        };

    private static DTOs.Ordering.CartLineDto ToLineDto(CartLine line)
    {
        var variant = line.ProductVariant;
        var product = variant.Product;

        var unitPrice = variant.EffectivePrice;

        return new DTOs.Ordering.CartLineDto
        {
            Id = line.Id,
            VariantId = variant.Id,
            ProductId = product.Id,
            ProductNameEn = product.Name.En,
            ProductNameBn = product.Name.Bn,
            ProductSlug = product.Slug.Value,
            Sku = variant.Sku,
            VariantName = variant.VariantName,
            PrimaryImagePath = PrimaryImage(product),
            Quantity = line.Quantity,
            UnitPrice = unitPrice.Amount,
            UnitPriceAtAdd = line.UnitPriceAtAdd.Amount,

            // Compared on amount, not on the Money instances: two equal amounts
            // in the same currency are the same price regardless of which
            // object holds them.
            PriceChanged = unitPrice.Amount != line.UnitPriceAtAdd.Amount,

            LineTotal = unitPrice.Multiply(line.Quantity).Amount,
            // Withdrawn, or sold out. Both are shown greyed out on the basket
            // page and both are refused at checkout; the message differs.
            IsAvailable = variant.IsActive
                          && product.Status == ProductStatus.Active
                          && Availability.IsInStock(product, variant),
            IsSoldOut = variant.IsActive
                        && product.Status == ProductStatus.Active
                        && !Availability.IsInStock(product, variant),
            AvailableQuantity = Availability.ScarceQuantity(product, variant),

            // Only meaningful for something being built to order. Sending it on
            // a stocked item would put "ready in 14 days" next to a lamp that
            // is on a shelf.
            LeadTimeDays = product.ProductType == ProductType.MadeToOrder
                ? product.LeadTimeDays
                : null
        };
    }

    private static DTOs.Ordering.CartTotalsDto ToTotalsDto(CartTotals totals, PricingContext context) =>
        new()
        {
            Subtotal = totals.Subtotal.Amount,
            DiscountTotal = totals.DiscountTotal.Amount,
            GoodsNet = totals.GoodsNet.Amount,
            VatAmount = totals.VatAmount.Amount,
            DeliveryFee = totals.DeliveryFee.Amount,
            GrandTotal = totals.GrandTotal.Amount,
            DeliveryWaived = totals.DeliveryWaived,

            // Adjusted by hand, so the page can say so rather than leaving the
            // customer to wonder why the arithmetic does not match the items.
            DeliveryOverridden = totals.DeliveryOverridden,

            // No zone chosen yet, so the delivery line is not zero — it is
            // unknown, and the page must say so.
            DeliveryPending = totals.DeliveryPending,

            ItemCount = totals.ItemCount,
            PricesIncludeVat = context.PricesIncludeVat
        };

    /// <summary>
    /// The product's primary image path, or null.
    /// </summary>
    /// <remarks>
    /// The query filters the include to primary media, so this is a scan of at
    /// most one element. It re-checks the flag anyway, because a mapper that
    /// silently depends on how it was queried breaks the first time somebody
    /// calls it from a different query.
    /// </remarks>
    private static string? PrimaryImage(Product product) =>
        product.Media.FirstOrDefault(m => m.IsPrimary)?.StoragePath;
}
