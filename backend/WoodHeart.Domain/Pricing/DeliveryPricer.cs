using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Domain.Pricing;

/// <summary>
/// What the delivery line comes to, and why.
/// </summary>
/// <param name="Fee">The amount charged.</param>
/// <param name="Waived">The free-delivery threshold took the charge off.</param>
/// <param name="Overridden">
/// Staff set the figure by hand, so it is not the sum of the products' own
/// charges.
/// </param>
/// <param name="Pending">
/// No zone has been chosen yet, so this is not a price — it is a blank the
/// checkout still has to fill in.
/// </param>
public readonly record struct DeliveryQuote(
    Money Fee,
    bool Waived,
    bool Overridden,
    bool Pending);

/// <summary>
/// Prices delivery from the products being delivered.
/// </summary>
/// <remarks>
/// <para>
/// <b>Delivery is a product-level cost here, not an order-level one.</b> The
/// obvious model — one flat charge per zone — is wrong for a furniture shop in
/// both directions at once: it loses money on every wardrobe and prices every
/// lamp out of the market. So each product carries what it costs to carry, per
/// zone, and the order's delivery is the sum across the basket.
/// </para>
/// <para>
/// A product that has not been costed yet falls back to the store default for
/// the zone. <b>Null means "the ordinary rate", never "free"</b> — a field
/// somebody forgot to fill in must not quietly become a giveaway on the most
/// expensive thing in the catalogue.
/// </para>
/// <para>
/// <b>Staff can override the total, and that is the point of the override
/// rather than an escape hatch.</b> Summing per-product charges is right when
/// the items are separate jobs and wrong when they are not: a bed and its two
/// bedside tables go in one pickup, on one trip, with the same two men. Nobody
/// can express that rule in a rate card, but the person looking at the order
/// can see it in a second — so the computed figure is a starting point they can
/// correct, and the corrected figure is what the customer is charged.
/// </para>
/// <para>
/// Separated from <see cref="CartPricer"/> because it is the part most likely
/// to grow — weight bands, courier rates, a distance lookup — and none of that
/// should be able to disturb the VAT arithmetic next door.
/// </para>
/// </remarks>
public static class DeliveryPricer
{
    public static DeliveryQuote Quote(
        IReadOnlyList<PricedLine> lines, PricingContext context, Money goodsTotal)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var currency = goodsTotal.Currency;
        var zero = Money.Zero(currency);

        // An override stands whatever else is true — including the threshold.
        // Staff who set a figure by hand have looked at the order; a rule that
        // silently replaced their number would make the field untrustworthy,
        // and a field nobody trusts stops being used.
        if (context.DeliveryFeeOverride is { } manual)
        {
            return new DeliveryQuote(manual.OrZeroIfNegative(), false, true, false);
        }

        // Zero, but flagged as pending rather than free: the cart page says
        // "calculated at checkout". Quoting a Dhaka rate to somebody in Sylhet
        // and raising it once they type their address is the surprise that
        // loses the order.
        if (context.Zone is not { } zone)
        {
            return new DeliveryQuote(zero, false, false, true);
        }

        if (context.FreeDeliveryThreshold is { IsPositive: true } threshold
            && goodsTotal >= threshold)
        {
            // Waives the whole charge, not part of it. With delivery priced per
            // product there is no "ordinary carriage" left to separate out —
            // which is worth knowing before switching the threshold on, because
            // it means giving away a wardrobe's carriage to Sylhet as well.
            return new DeliveryQuote(zero, true, false, false);
        }

        var fee = lines.Aggregate(zero, (running, line) =>
            running + ChargeFor(line, zone, context.DefaultDeliveryCharge, currency));

        return new DeliveryQuote(fee, false, false, false);
    }

    /// <summary>
    /// One line's contribution: the product's charge for the zone, or the store
    /// default, times the quantity.
    /// </summary>
    /// <remarks>
    /// Multiplied by quantity because two beds are two beds. Where they are not
    /// — two items that genuinely travel together — that is what the override
    /// is for.
    /// </remarks>
    private static Money ChargeFor(
        PricedLine line, DeliveryZone zone, Money? storeDefault, string currency)
    {
        var perUnit = zone == DeliveryZone.InsideDhaka
            ? line.DeliveryChargeInsideDhaka
            : line.DeliveryChargeOutsideDhaka;

        perUnit ??= storeDefault;

        return perUnit is null
            ? Money.Zero(currency)
            : perUnit.OrZeroIfNegative().Multiply(line.Quantity);
    }
}
