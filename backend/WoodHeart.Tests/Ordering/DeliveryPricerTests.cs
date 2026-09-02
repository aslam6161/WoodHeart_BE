using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.Pricing;
using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Tests.Ordering;

/// <summary>
/// What delivery costs, and who decides.
/// </summary>
/// <remarks>
/// <para>
/// Delivery is priced per product rather than per order, because a furniture
/// shop cannot use one flat rate: it loses money on every wardrobe and prices
/// every lamp out of the market. So each product carries what it costs to
/// carry, per zone, and the order's charge is the sum.
/// </para>
/// <para>
/// The two rules most likely to be got wrong, and therefore the most heavily
/// tested here: <b>a blank charge means the store default, never free</b>, and
/// <b>a staff override beats everything</b> — including the free-delivery
/// threshold.
/// </para>
/// </remarks>
public class DeliveryPricerTests
{
    private static PricedLine Line(
        int quantity = 1, decimal? inside = null, decimal? outside = null, decimal price = 1000m) =>
        new(quantity,
            Money.Taka(price),
            inside is { } i ? Money.Taka(i) : null,
            outside is { } o ? Money.Taka(o) : null);

    private static PricingContext Context(
        DeliveryZone? zone = DeliveryZone.InsideDhaka,
        decimal? storeDefault = null,
        decimal? threshold = null,
        decimal? overrideFee = null) =>
        new(0m, true,
            Zone: zone,
            DefaultDeliveryCharge: storeDefault is { } d ? Money.Taka(d) : null,
            FreeDeliveryThreshold: threshold is { } t ? Money.Taka(t) : null,
            DeliveryFeeOverride: overrideFee is { } o ? Money.Taka(o) : null);

    private static Money Goods(decimal amount) => Money.Taka(amount);

    // -------------------------------------------------------------------------
    // Per-product charges
    // -------------------------------------------------------------------------

    [Fact]
    public void Each_products_charge_is_added_up()
    {
        // A bed and a wardrobe are two separate jobs, so the customer pays for
        // both. Where they are not two jobs, that is what the override is for.
        var quote = DeliveryPricer.Quote(
            [Line(inside: 500m), Line(inside: 800m), Line(inside: 0m)],
            Context(),
            Goods(111_200m));

        quote.Fee.Amount.ShouldBe(1300m);
        quote.Overridden.ShouldBeFalse();
        quote.Waived.ShouldBeFalse();
    }

    [Fact]
    public void A_charge_applies_per_unit()
    {
        // Two beds are two beds — both have to be carried up the same stairs.
        var quote = DeliveryPricer.Quote([Line(quantity: 3, inside: 500m)], Context(), Goods(30_000m));

        quote.Fee.Amount.ShouldBe(1500m);
    }

    [Fact]
    public void The_zone_decides_which_of_the_two_charges_is_used()
    {
        var lines = new[] { Line(inside: 800m, outside: 2500m) };

        DeliveryPricer.Quote(lines, Context(DeliveryZone.InsideDhaka), Goods(60_000m))
            .Fee.Amount.ShouldBe(800m);

        // Not a marginal difference — sending a wardrobe to Sylhet is a
        // different job, which is why the two are separate columns.
        DeliveryPricer.Quote(lines, Context(DeliveryZone.OutsideDhaka), Goods(60_000m))
            .Fee.Amount.ShouldBe(2500m);
    }

    // -------------------------------------------------------------------------
    // The fallback — the rule with the most expensive failure mode
    // -------------------------------------------------------------------------

    [Fact]
    public void A_product_with_no_charge_falls_back_to_the_store_default()
    {
        var quote = DeliveryPricer.Quote(
            [Line(inside: null)], Context(storeDefault: 150m), Goods(4000m));

        quote.Fee.Amount.ShouldBe(150m);
    }

    [Fact]
    public void A_blank_charge_never_means_free_delivery()
    {
        // The most expensive mistake available here: an admin adds a wardrobe,
        // leaves the delivery field empty, and the shop carries it across the
        // country for nothing. Blank is "the ordinary rate", not zero.
        var quote = DeliveryPricer.Quote(
            [Line(inside: null, price: 62_000m)], Context(storeDefault: 200m), Goods(62_000m));

        quote.Fee.Amount.ShouldBe(200m);
        quote.Fee.IsZero.ShouldBeFalse();
    }

    [Fact]
    public void An_explicit_zero_does_mean_free_delivery()
    {
        // Distinct from blank, and deliberately so — a wall print genuinely
        // ships free, and the admin says so by typing 0 rather than by leaving
        // the field alone.
        var quote = DeliveryPricer.Quote(
            [Line(inside: 0m)], Context(storeDefault: 200m), Goods(4200m));

        quote.Fee.Amount.ShouldBe(0m);
    }

    [Fact]
    public void Costed_and_uncosted_products_mix_in_one_basket()
    {
        var quote = DeliveryPricer.Quote(
            [Line(inside: 500m), Line(inside: null), Line(quantity: 2, inside: null)],
            Context(storeDefault: 100m),
            Goods(20_000m));

        // 500 + 100 + (100 × 2)
        quote.Fee.Amount.ShouldBe(800m);
    }

    [Fact]
    public void With_no_charge_and_no_default_delivery_is_free_rather_than_an_error()
    {
        // The seeded state before anybody has entered any figures. It must
        // produce a bill, not an exception — the shop can then see zeros and
        // fix them.
        var quote = DeliveryPricer.Quote([Line(inside: null)], Context(), Goods(1000m));

        quote.Fee.Amount.ShouldBe(0m);
    }

    // -------------------------------------------------------------------------
    // Pending, waived, overridden
    // -------------------------------------------------------------------------

    [Fact]
    public void With_no_zone_chosen_delivery_is_pending_not_free()
    {
        var quote = DeliveryPricer.Quote(
            [Line(inside: 500m)], Context(zone: null, storeDefault: 150m), Goods(45_000m));

        quote.Fee.Amount.ShouldBe(0m);

        // The distinction the cart page needs: it shows "calculated at
        // checkout" rather than a zero that later becomes 500.
        quote.Pending.ShouldBeTrue();
        quote.Waived.ShouldBeFalse();
    }

    [Fact]
    public void Reaching_the_threshold_waives_the_whole_charge()
    {
        var quote = DeliveryPricer.Quote(
            [Line(inside: 500m), Line(inside: 800m)],
            Context(threshold: 10_000m),
            Goods(10_000m));

        // At the threshold, not merely above it: "free delivery over 10,000৳"
        // refusing at exactly 10,000৳ is a complaint the shop would deserve.
        quote.Fee.Amount.ShouldBe(0m);
        quote.Waived.ShouldBeTrue();
    }

    [Fact]
    public void Below_the_threshold_every_charge_stands()
    {
        var quote = DeliveryPricer.Quote(
            [Line(inside: 500m)], Context(threshold: 10_000m), Goods(9999m));

        quote.Fee.Amount.ShouldBe(500m);
        quote.Waived.ShouldBeFalse();
    }

    [Fact]
    public void A_zero_threshold_disables_the_rule_rather_than_making_everything_free()
    {
        // The seeded default is zero. Read as "everything qualifies" it would
        // give away delivery on every order in the shop.
        var quote = DeliveryPricer.Quote(
            [Line(inside: 500m)], Context(threshold: 0m), Goods(100m));

        quote.Fee.Amount.ShouldBe(500m);
        quote.Waived.ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // The override — "those two fit in the same van"
    // -------------------------------------------------------------------------

    [Fact]
    public void Staff_can_replace_the_calculated_charge()
    {
        // A bed and its two bedside tables: three charges by the rate card,
        // one pickup in reality. Nobody can express that as a rule, and the
        // person looking at the order can see it in a second.
        var quote = DeliveryPricer.Quote(
            [Line(inside: 800m), Line(quantity: 2, inside: 300m)],
            Context(overrideFee: 900m),
            Goods(70_000m));

        // 800 + 600 = 1,400 by the rate card; charged 900.
        quote.Fee.Amount.ShouldBe(900m);
        quote.Overridden.ShouldBeTrue();
    }

    [Fact]
    public void An_override_of_zero_is_honoured()
    {
        // "We were passing anyway." Distinct from no override at all, so the
        // check has to be for null rather than for a falsy amount.
        var quote = DeliveryPricer.Quote(
            [Line(inside: 800m)], Context(overrideFee: 0m), Goods(45_000m));

        quote.Fee.Amount.ShouldBe(0m);
        quote.Overridden.ShouldBeTrue();
        quote.Waived.ShouldBeFalse();
    }

    [Fact]
    public void An_override_beats_the_free_delivery_threshold()
    {
        // Both could apply; the override wins. Silently replacing a figure a
        // person entered would make the field untrustworthy, and a field nobody
        // trusts stops being used.
        var quote = DeliveryPricer.Quote(
            [Line(inside: 500m)],
            Context(threshold: 10_000m, overrideFee: 250m),
            Goods(50_000m));

        quote.Fee.Amount.ShouldBe(250m);
        quote.Overridden.ShouldBeTrue();
        quote.Waived.ShouldBeFalse();
    }

    [Fact]
    public void An_override_applies_even_before_a_zone_is_chosen()
    {
        // A phone order priced by hand does not wait for the customer to pick
        // a zone in a UI they are not using.
        var quote = DeliveryPricer.Quote(
            [Line(inside: 500m)], Context(zone: null, overrideFee: 400m), Goods(45_000m));

        quote.Fee.Amount.ShouldBe(400m);
        quote.Overridden.ShouldBeTrue();
        quote.Pending.ShouldBeFalse();
    }

    [Fact]
    public void A_negative_override_is_clamped_to_zero()
    {
        // A typed minus sign must not pay the customer to accept a delivery.
        var quote = DeliveryPricer.Quote(
            [Line(inside: 500m)], Context(overrideFee: -250m), Goods(45_000m));

        quote.Fee.Amount.ShouldBe(0m);
        quote.Fee.IsNegative.ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // Edges
    // -------------------------------------------------------------------------

    [Fact]
    public void An_empty_basket_has_nothing_to_deliver()
    {
        var quote = DeliveryPricer.Quote([], Context(storeDefault: 150m), Goods(0m));

        quote.Fee.Amount.ShouldBe(0m);
    }

    [Fact]
    public void A_negative_product_charge_is_ignored_rather_than_credited()
    {
        var quote = DeliveryPricer.Quote([Line(inside: -100m)], Context(), Goods(1000m));

        quote.Fee.Amount.ShouldBe(0m);
    }
}
