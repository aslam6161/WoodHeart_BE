using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Domain.Pricing;

/// <summary>
/// One line as the pricer sees it: a quantity, a unit price, and what it costs
/// to deliver.
/// </summary>
/// <remarks>
/// A flat record rather than the entity, so the pricer never touches EF
/// navigations and cannot accidentally trigger a lazy load in the middle of a
/// calculation.
/// </remarks>
/// <param name="Quantity">How many. Must be positive.</param>
/// <param name="UnitPrice">The variant's live price, per unit.</param>
/// <param name="DeliveryChargeInsideDhaka">
/// What one of these costs to deliver inside Dhaka. Null means the product has
/// not been costed and the store default applies — never that it ships free.
/// </param>
/// <param name="DeliveryChargeOutsideDhaka">The same, for everywhere else.</param>
public readonly record struct PricedLine(
    int Quantity,
    Money UnitPrice,
    Money? DeliveryChargeInsideDhaka = null,
    Money? DeliveryChargeOutsideDhaka = null)
{
    /// <summary>Unit price times quantity, before any discount.</summary>
    public Money LineTotal => UnitPrice.Multiply(Quantity);
}

/// <summary>
/// Everything outside the lines that changes the bill: the tax regime, where
/// the order is going, and any figure already decided by hand.
/// </summary>
/// <param name="VatRatePercent">
/// e.g. <c>7.5</c> for 7.5%. Zero switches VAT off entirely.
/// </param>
/// <param name="PricesIncludeVat">
/// Whether catalog prices already contain VAT. <b>This flag inverts the
/// arithmetic</b> — see the class remarks on <see cref="CartPricer"/>.
/// </param>
/// <param name="Zone">
/// Where it is being delivered, or null when the customer has not said yet and
/// delivery is still "calculated at checkout".
/// </param>
/// <param name="DefaultDeliveryCharge">
/// The store's ordinary rate for the zone, used for any product that has not
/// been costed individually.
/// </param>
/// <param name="FreeDeliveryThreshold">
/// Goods total at or above which delivery is waived. Null or zero disables the
/// rule — see <see cref="DeliveryPricer"/> for why that default matters now
/// that delivery is priced per product.
/// </param>
/// <param name="DeliveryFeeOverride">
/// A figure set by staff, which replaces the calculated one. This is how "both
/// of those fit in the same van" gets expressed.
/// </param>
/// <param name="VatOnDelivery">
/// Whether the delivery charge is itself taxable. Defaults to false; a business
/// question rather than a code one.
/// </param>
/// <param name="Discount">
/// Money already taken off the goods, resolved elsewhere. Zero until the
/// discount engine arrives in Phase 3; it is a parameter now so that adding
/// discounts does not change this function's shape or its tests.
/// </param>
public readonly record struct PricingContext(
    decimal VatRatePercent,
    bool PricesIncludeVat,
    DeliveryZone? Zone = null,
    Money? DefaultDeliveryCharge = null,
    Money? FreeDeliveryThreshold = null,
    Money? DeliveryFeeOverride = null,
    bool VatOnDelivery = false,
    Money? Discount = null);

/// <summary>
/// The bill, broken into the lines a customer expects to see.
/// </summary>
/// <remarks>
/// <see cref="GoodsNet"/> plus <see cref="VatAmount"/> plus
/// <see cref="DeliveryFee"/> always equals <see cref="GrandTotal"/> exactly, in
/// both tax regimes. That identity is a test, not a hope — see
/// <c>CartPricerTests</c>.
/// </remarks>
public readonly record struct CartTotals(
    Money Subtotal,
    Money DiscountTotal,
    Money GoodsNet,
    Money VatAmount,
    Money DeliveryFee,
    Money GrandTotal,
    bool DeliveryWaived,
    bool DeliveryOverridden,
    bool DeliveryPending,
    int ItemCount);

/// <summary>
/// Turns cart lines into a bill. Pure: no database, no clock, no configuration
/// lookups.
/// </summary>
/// <remarks>
/// <para>
/// Everything this function needs arrives in <see cref="PricingContext"/>, so
/// it is exhaustively testable and — the reason that actually matters — the
/// <i>same</i> function runs at cart preview and at order placement. Two
/// implementations of "what does this cost" always drift, and when they drift
/// the customer sees one number and gets charged another.
/// </para>
/// <para>
/// <b>The inclusive/exclusive distinction is the whole difficulty.</b>
/// Bangladeshi retail quotes prices VAT-inclusive: a 5,000৳ price tag means the
/// customer hands over 5,000৳ and the VAT is already inside it. So VAT is
/// <i>extracted</i>:
/// </para>
/// <code>
/// vat = gross × rate / (100 + rate)      // inclusive — 7.5% of 5,000 is 348.84
/// vat = net   × rate / 100               // exclusive — 7.5% of 5,000 is 375.00
/// </code>
/// <para>
/// Applying the exclusive formula to inclusive prices overcharges every single
/// customer by the VAT rate, and it looks entirely plausible while doing it.
/// That is why the flag is explicit in the context rather than assumed.
/// </para>
/// <para>
/// <b>Rounding order matters.</b> VAT is rounded once and the net figure is
/// derived by subtraction, never the other way round. Rounding both
/// independently lets net + VAT come to a poisha more or less than the gross,
/// and an invoice whose lines do not add up to its own total is the kind of
/// thing an auditor stops on.
/// </para>
/// <para>
/// Delivery is <see cref="DeliveryPricer"/>'s job, not this one's — it is the
/// part most likely to grow, and it should not be able to disturb the tax
/// arithmetic when it does.
/// </para>
/// </remarks>
public static class CartPricer
{
    public static CartTotals Price(IReadOnlyList<PricedLine> lines, PricingContext context)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var currency = lines.Count > 0 ? lines[0].UnitPrice.Currency : Money.Bdt;
        var zero = Money.Zero(currency);

        var subtotal = lines.Aggregate(zero, (running, line) => running + line.LineTotal);
        var itemCount = lines.Sum(line => line.Quantity);

        // Clamped, because a discount larger than the basket must make the goods
        // free — never negative. A negative goods total would flow into VAT and
        // then into the grand total, and the customer would be owed money by a
        // shop that has not shipped anything.
        var discount = (context.Discount ?? zero).OrZeroIfNegative().CapAt(subtotal);
        var goodsGross = (subtotal - discount).OrZeroIfNegative();

        var delivery = DeliveryPricer.Quote(lines, context, goodsGross);

        var taxableBase = context.VatOnDelivery ? goodsGross + delivery.Fee : goodsGross;
        var vat = Vat(taxableBase, context);

        // Both regimes converge here, and the difference is only what the
        // customer's price tag already contained:
        //
        //   inclusive — the VAT is inside goodsGross, so the total is unchanged
        //               by tax and the net is what is left after extracting it.
        //   exclusive — the VAT is added on top, so the total grows by it.
        var grandTotal = context.PricesIncludeVat
            ? goodsGross + delivery.Fee
            : goodsGross + delivery.Fee + vat;

        var goodsNet = (grandTotal - delivery.Fee - vat).OrZeroIfNegative();

        return new CartTotals(
            Subtotal: subtotal,
            DiscountTotal: discount,
            GoodsNet: goodsNet,
            VatAmount: vat,
            DeliveryFee: delivery.Fee,
            GrandTotal: grandTotal,
            DeliveryWaived: delivery.Waived,
            DeliveryOverridden: delivery.Overridden,
            DeliveryPending: delivery.Pending,
            ItemCount: itemCount);
    }

    /// <summary>
    /// Extracts VAT from an inclusive amount, or calculates it on top of an
    /// exclusive one.
    /// </summary>
    private static Money Vat(Money taxableBase, PricingContext context)
    {
        var rate = context.VatRatePercent;

        if (rate <= 0m || !taxableBase.IsPositive)
        {
            return Money.Zero(taxableBase.Currency);
        }

        return context.PricesIncludeVat
            ? taxableBase.Multiply(rate / (100m + rate))
            : taxableBase.Percentage(rate);
    }
}
