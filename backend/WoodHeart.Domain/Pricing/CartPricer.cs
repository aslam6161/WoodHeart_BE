using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Domain.Pricing;

/// <summary>
/// One line as the pricer sees it: a quantity, a unit price, and whatever about
/// the product changes what delivering it costs.
/// </summary>
/// <remarks>
/// A flat record rather than the entity, so the pricer never touches EF
/// navigations and cannot accidentally trigger a lazy load in the middle of a
/// calculation.
/// </remarks>
/// <param name="Quantity">How many. Must be positive.</param>
/// <param name="UnitPrice">The variant's live price, per unit.</param>
/// <param name="DeliverySurchargePerUnit">
/// Extra carriage for something bulky, per unit, on top of the zone rate. A
/// three-seater sofa is not a table lamp.
/// </param>
public readonly record struct PricedLine(
    int Quantity,
    Money UnitPrice,
    Money? DeliverySurchargePerUnit = null)
{
    /// <summary>Unit price times quantity, before any discount.</summary>
    public Money LineTotal => UnitPrice.Multiply(Quantity);

    /// <summary>This line's contribution to the surcharge, or zero.</summary>
    public Money SurchargeTotal =>
        DeliverySurchargePerUnit is null
            ? Money.Zero(UnitPrice.Currency)
            : DeliverySurchargePerUnit.Multiply(Quantity);
}

/// <summary>
/// Everything outside the lines that changes the bill: the tax regime, the
/// delivery rate for the chosen zone, and any discount already resolved.
/// </summary>
/// <param name="VatRatePercent">
/// e.g. <c>15</c> for 15%. Zero switches VAT off entirely, which is the seeded
/// default until the real rate is confirmed.
/// </param>
/// <param name="PricesIncludeVat">
/// Whether catalog prices already contain VAT. <b>This flag inverts the
/// arithmetic</b> — see the class remarks on <see cref="CartPricer"/>.
/// </param>
/// <param name="ZoneDeliveryCharge">
/// The flat charge for the delivery zone, or null when the customer has not
/// chosen one yet and delivery is still "calculated at checkout".
/// </param>
/// <param name="FreeDeliveryThreshold">
/// Goods total at or above which the zone charge is waived. Null or zero
/// disables the rule.
/// </param>
/// <param name="VatOnDelivery">
/// Whether the delivery charge is itself taxable. Defaults to false; see the
/// remarks on <see cref="CartPricer"/> for why this is a setting rather than a
/// decision baked into the code.
/// </param>
/// <param name="Discount">
/// Money already taken off the goods, resolved elsewhere. Zero until the
/// discount engine arrives in Phase 3; it is a parameter now so that adding
/// discounts does not change this function's shape or its tests.
/// </param>
public readonly record struct PricingContext(
    decimal VatRatePercent,
    bool PricesIncludeVat,
    Money? ZoneDeliveryCharge = null,
    Money? FreeDeliveryThreshold = null,
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
/// vat = gross × rate / (100 + rate)      // inclusive — 15% of 5,000 is 652.17
/// vat = net   × rate / 100               // exclusive — 15% of 5,000 is 750.00
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
/// <b>Whether delivery is taxable is a business question, not a code
/// question</b> (PLAN.md §16.1), so it is a setting with a documented default
/// of false rather than an assumption buried in an expression.
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

        var (deliveryFee, waived) = Delivery(lines, context, goodsGross, zero);

        var taxableBase = context.VatOnDelivery ? goodsGross + deliveryFee : goodsGross;
        var vat = Vat(taxableBase, context);

        // Both regimes converge here, and the difference is only what the
        // customer's price tag already contained:
        //
        //   inclusive — the VAT is inside goodsGross, so the total is unchanged
        //               by tax and the net is what is left after extracting it.
        //   exclusive — the VAT is added on top, so the total grows by it.
        var grandTotal = context.PricesIncludeVat
            ? goodsGross + deliveryFee
            : goodsGross + deliveryFee + vat;

        var goodsNet = (grandTotal - deliveryFee - vat).OrZeroIfNegative();

        return new CartTotals(
            Subtotal: subtotal,
            DiscountTotal: discount,
            GoodsNet: goodsNet,
            VatAmount: vat,
            DeliveryFee: deliveryFee,
            GrandTotal: grandTotal,
            DeliveryWaived: waived,
            ItemCount: itemCount);
    }

    /// <summary>
    /// The zone charge, waived above the free-delivery threshold, plus any
    /// per-product surcharge.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The threshold waives the zone charge but never the surcharge.</b>
    /// "Free delivery over 10,000৳" is a marketing promise about ordinary
    /// carriage; a wardrobe that needs two men and a pickup still costs what it
    /// costs, and folding that into a free-delivery offer means the shop pays to
    /// deliver its most expensive-to-move items. If that turns out to be the
    /// wrong call commercially it is one line to change — but it should be
    /// changed deliberately.
    /// </para>
    /// <para>
    /// Delivery is zero, not "unknown", while the zone is unset. The cart page
    /// shows it as calculated at checkout; guessing at Dhaka would quote a
    /// Chattogram customer a price that goes up when they enter their address.
    /// </para>
    /// </remarks>
    private static (Money Fee, bool Waived) Delivery(
        IReadOnlyList<PricedLine> lines, PricingContext context, Money goodsGross, Money zero)
    {
        if (context.ZoneDeliveryCharge is not { } zoneCharge)
        {
            return (zero, false);
        }

        var waived = context.FreeDeliveryThreshold is { IsPositive: true } threshold
                     && goodsGross >= threshold;

        var surcharge = lines.Aggregate(zero, (running, line) => running + line.SurchargeTotal);

        return (waived ? surcharge : zoneCharge + surcharge, waived);
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
