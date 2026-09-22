using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Enums.Promotions;
using WoodHeart.Domain.Pricing;
using WoodHeart.Domain.Promotions;
using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Service.Services.Promotions;

/// <summary>
/// The small amount of glue between a basket and the discount engine.
/// </summary>
/// <remarks>
/// Shared between the basket page and order placement on purpose. Both have to
/// hand the engine the <i>same</i> lines, and a second copy of "which lines
/// count and what is their category" is a second chance to disagree about what
/// a customer was shown.
/// </remarks>
public static class CartPromotions
{
    /// <summary>Cart lines as the engine sees them.</summary>
    /// <remarks>
    /// The category path comes off the product's category, which the cart
    /// repository includes for this. Empty when it was not loaded — a discount
    /// targeted at a category then matches nothing, which is the safe way for
    /// that mistake to fail.
    /// </remarks>
    public static IReadOnlyList<DiscountLine> LinesFrom(IEnumerable<CartLine> lines) =>
    [
        .. lines.Select(line => new DiscountLine(
            VariantId: line.ProductVariantId,
            ProductId: line.ProductVariant.ProductId,
            CategoryPath: line.ProductVariant.Product.Category?.MaterializedPath ?? string.Empty,
            Quantity: line.Quantity,
            LineTotal: line.ProductVariant.EffectivePrice.Multiply(line.Quantity)))
    ];

    /// <summary>
    /// Fills in what a free-shipping discount was actually worth.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The engine values free shipping at what delivery would have cost, which
    /// is right nearly always and wrong in one case: staff have already set the
    /// delivery charge by hand, that figure stands, and the coupon waives
    /// nothing. Taking the difference between the two pricing passes is exact
    /// in every case, including that one.
    /// </para>
    /// <para>
    /// It matters because this number is what the order records, what the usage
    /// report sums, and what the shop uses to decide whether a promotion was
    /// worth running.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<AppliedDiscount> Settle(
        DiscountOutcome outcome, CartTotals before, CartTotals after) =>
    [
        .. outcome.Applied.Select(discount => discount.Type == DiscountType.FreeShipping
            ? discount with { Amount = (before.DeliveryFee - after.DeliveryFee).OrZeroIfNegative() }
            : discount)
    ];

    /// <summary>
    /// What each line had taken off it, summed across every discount that
    /// applied.
    /// </summary>
    /// <remarks>
    /// Two stackable discounts can both touch the same sofa, and the order line
    /// stores one figure — so they add. The total across the dictionary equals
    /// the order's <c>DiscountTotal</c>, which is what makes a partial refund
    /// arithmetically possible.
    /// </remarks>
    public static IReadOnlyDictionary<long, Money> PerVariant(
        IReadOnlyList<AppliedDiscount> applied)
    {
        var totals = new Dictionary<long, Money>();

        foreach (var discount in applied)
        {
            foreach (var (variantId, amount) in discount.PerVariant)
            {
                totals[variantId] = totals.TryGetValue(variantId, out var running)
                    ? running + amount
                    : amount;
            }
        }

        return totals;
    }
}
