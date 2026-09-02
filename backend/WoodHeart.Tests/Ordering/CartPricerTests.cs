using WoodHeart.Domain.Pricing;
using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Tests.Ordering;

/// <summary>
/// What a customer is actually charged.
/// </summary>
/// <remarks>
/// <para>
/// The most heavily tested thing in the codebase after <c>Money</c>, and for
/// the same reason: every bug here is money, in one direction or the other, on
/// every order. The pricer is a pure function precisely so that it can be
/// covered this thoroughly without a database.
/// </para>
/// <para>
/// The case that motivates most of these is VAT-inclusive pricing. Bangladeshi
/// retail quotes a price the customer hands over whole, so VAT is extracted
/// from it rather than added to it — and the wrong formula overcharges every
/// order by the VAT rate while looking entirely reasonable.
/// </para>
/// </remarks>
public class CartPricerTests
{
    private static PricedLine Line(decimal unitPrice, int quantity = 1, decimal? surcharge = null) =>
        new(quantity, Money.Taka(unitPrice), surcharge is { } s ? Money.Taka(s) : null);

    // -------------------------------------------------------------------------
    // The basics
    // -------------------------------------------------------------------------

    [Fact]
    public void An_empty_cart_costs_nothing()
    {
        var totals = CartPricer.Price([], new PricingContext(15m, PricesIncludeVat: true));

        totals.Subtotal.Amount.ShouldBe(0m);
        totals.GrandTotal.Amount.ShouldBe(0m);
        totals.VatAmount.Amount.ShouldBe(0m);
        totals.ItemCount.ShouldBe(0);
    }

    [Fact]
    public void Subtotal_multiplies_price_by_quantity()
    {
        var totals = CartPricer.Price(
            [Line(4500m, 2), Line(1200m, 3)],
            new PricingContext(0m, PricesIncludeVat: true));

        totals.Subtotal.Amount.ShouldBe(12_600m);
        totals.ItemCount.ShouldBe(5);
    }

    [Fact]
    public void A_zero_vat_rate_charges_no_vat_in_either_regime()
    {
        // The seeded default until the real rate is confirmed, so it must be
        // exactly the same bill either way round.
        var inclusive = CartPricer.Price([Line(5000m)], new PricingContext(0m, true));
        var exclusive = CartPricer.Price([Line(5000m)], new PricingContext(0m, false));

        inclusive.VatAmount.Amount.ShouldBe(0m);
        exclusive.VatAmount.Amount.ShouldBe(0m);
        inclusive.GrandTotal.Amount.ShouldBe(5000m);
        exclusive.GrandTotal.Amount.ShouldBe(5000m);
    }

    // -------------------------------------------------------------------------
    // VAT — the part that is easy to get backwards
    // -------------------------------------------------------------------------

    [Fact]
    public void Inclusive_vat_is_extracted_from_the_price_not_added_to_it()
    {
        var totals = CartPricer.Price([Line(5000m)], new PricingContext(15m, PricesIncludeVat: true));

        // 5000 × 15/115. The customer hands over exactly the price on the tag.
        totals.VatAmount.Amount.ShouldBe(652.17m);
        totals.GoodsNet.Amount.ShouldBe(4347.83m);
        totals.GrandTotal.Amount.ShouldBe(5000m);
    }

    [Fact]
    public void Exclusive_vat_is_added_on_top()
    {
        var totals = CartPricer.Price([Line(5000m)], new PricingContext(15m, PricesIncludeVat: false));

        totals.VatAmount.Amount.ShouldBe(750m);
        totals.GoodsNet.Amount.ShouldBe(5000m);
        totals.GrandTotal.Amount.ShouldBe(5750m);
    }

    [Fact]
    public void Using_the_exclusive_formula_on_inclusive_prices_would_overcharge()
    {
        // Not a test of behaviour so much as a guard on the distinction itself:
        // if these two ever come out equal, the flag has stopped meaning
        // anything and every customer is being charged the wrong amount.
        var inclusive = CartPricer.Price([Line(5000m)], new PricingContext(15m, true));
        var exclusive = CartPricer.Price([Line(5000m)], new PricingContext(15m, false));

        exclusive.GrandTotal.Amount.ShouldBeGreaterThan(inclusive.GrandTotal.Amount);
        (exclusive.GrandTotal.Amount - inclusive.GrandTotal.Amount).ShouldBe(750m);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Net_plus_vat_plus_delivery_always_equals_the_grand_total(bool inclusive)
    {
        // The identity an invoice has to satisfy. Rounding VAT and the net
        // figure independently breaks it by a poisha, and an invoice whose
        // lines do not add up to its own total is what an auditor stops on.
        var totals = CartPricer.Price(
            [Line(1333.33m, 3), Line(777.77m, 2)],
            new PricingContext(
                VatRatePercent: 15m,
                PricesIncludeVat: inclusive,
                ZoneDeliveryCharge: Money.Taka(150m)));

        var sum = totals.GoodsNet + totals.VatAmount + totals.DeliveryFee;

        sum.Amount.ShouldBe(totals.GrandTotal.Amount);
    }

    [Fact]
    public void Delivery_is_untaxed_unless_the_setting_says_otherwise()
    {
        var lines = new[] { Line(1000m) };
        var withoutTax = new PricingContext(15m, false, ZoneDeliveryCharge: Money.Taka(200m));
        var withTax = withoutTax with { VatOnDelivery = true };

        CartPricer.Price(lines, withoutTax).VatAmount.Amount.ShouldBe(150m);

        // 15% of 1,200 rather than of 1,000.
        CartPricer.Price(lines, withTax).VatAmount.Amount.ShouldBe(180m);
    }

    // -------------------------------------------------------------------------
    // Delivery
    // -------------------------------------------------------------------------

    [Fact]
    public void Delivery_is_zero_until_a_zone_is_chosen()
    {
        // Zero rather than a guess. Quoting the Dhaka rate to someone in Sylhet
        // and raising it at checkout is the surprise that loses the order.
        var totals = CartPricer.Price([Line(5000m)], new PricingContext(0m, true));

        totals.DeliveryFee.Amount.ShouldBe(0m);
        totals.DeliveryWaived.ShouldBeFalse();
    }

    [Fact]
    public void The_zone_charge_applies_once_however_many_items_there_are()
    {
        var totals = CartPricer.Price(
            [Line(1000m, 4), Line(500m, 2)],
            new PricingContext(0m, true, ZoneDeliveryCharge: Money.Taka(120m)));

        totals.DeliveryFee.Amount.ShouldBe(120m);
    }

    [Fact]
    public void Reaching_the_threshold_waives_the_zone_charge()
    {
        var totals = CartPricer.Price(
            [Line(10_000m)],
            new PricingContext(
                0m, true,
                ZoneDeliveryCharge: Money.Taka(120m),
                FreeDeliveryThreshold: Money.Taka(10_000m)));

        // At the threshold, not merely above it. "Free delivery over 10,000৳"
        // refusing at exactly 10,000৳ is the kind of detail customers complain
        // about and they are right to.
        totals.DeliveryFee.Amount.ShouldBe(0m);
        totals.DeliveryWaived.ShouldBeTrue();
    }

    [Fact]
    public void Below_the_threshold_the_charge_stands()
    {
        var totals = CartPricer.Price(
            [Line(9999m)],
            new PricingContext(
                0m, true,
                ZoneDeliveryCharge: Money.Taka(120m),
                FreeDeliveryThreshold: Money.Taka(10_000m)));

        totals.DeliveryFee.Amount.ShouldBe(120m);
        totals.DeliveryWaived.ShouldBeFalse();
    }

    [Fact]
    public void A_zero_threshold_disables_the_rule_rather_than_making_everything_free()
    {
        // The seeded default is zero. Read as "everything qualifies" it would
        // give away delivery on every order in the shop.
        var totals = CartPricer.Price(
            [Line(100m)],
            new PricingContext(
                0m, true,
                ZoneDeliveryCharge: Money.Taka(120m),
                FreeDeliveryThreshold: Money.Taka(0m)));

        totals.DeliveryFee.Amount.ShouldBe(120m);
        totals.DeliveryWaived.ShouldBeFalse();
    }

    [Fact]
    public void A_bulky_item_adds_its_surcharge_per_unit()
    {
        var totals = CartPricer.Price(
            [Line(45_000m, 2, surcharge: 500m), Line(1200m)],
            new PricingContext(0m, true, ZoneDeliveryCharge: Money.Taka(120m)));

        totals.DeliveryFee.Amount.ShouldBe(1120m);
    }

    [Fact]
    public void Free_delivery_waives_the_zone_charge_but_not_the_surcharge()
    {
        // The deliberate commercial call: "free delivery over 10,000৳" is a
        // promise about ordinary carriage. A wardrobe that needs two men and a
        // pickup still costs what it costs, and folding that into the offer
        // means the shop pays most to deliver exactly its bulkiest goods.
        var totals = CartPricer.Price(
            [Line(45_000m, surcharge: 500m)],
            new PricingContext(
                0m, true,
                ZoneDeliveryCharge: Money.Taka(120m),
                FreeDeliveryThreshold: Money.Taka(10_000m)));

        totals.DeliveryFee.Amount.ShouldBe(500m);
        totals.DeliveryWaived.ShouldBeTrue();
    }

    [Fact]
    public void The_threshold_is_measured_after_discount_not_before()
    {
        // A 9,000৳ basket does not earn free delivery by having been 11,000৳
        // before a coupon. Measuring before the discount lets a coupon buy the
        // delivery too.
        var totals = CartPricer.Price(
            [Line(11_000m)],
            new PricingContext(
                0m, true,
                ZoneDeliveryCharge: Money.Taka(120m),
                FreeDeliveryThreshold: Money.Taka(10_000m),
                Discount: Money.Taka(2000m)));

        totals.DeliveryFee.Amount.ShouldBe(120m);
        totals.DeliveryWaived.ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // Discounts — zero today, but the arithmetic has to be right when they land
    // -------------------------------------------------------------------------

    [Fact]
    public void A_discount_comes_off_the_goods_before_vat()
    {
        var totals = CartPricer.Price(
            [Line(5000m)],
            new PricingContext(15m, false, Discount: Money.Taka(1000m)));

        totals.DiscountTotal.Amount.ShouldBe(1000m);
        totals.GoodsNet.Amount.ShouldBe(4000m);
        totals.VatAmount.Amount.ShouldBe(600m);
        totals.GrandTotal.Amount.ShouldBe(4600m);
    }

    [Fact]
    public void A_discount_larger_than_the_basket_makes_it_free_not_negative()
    {
        var totals = CartPricer.Price(
            [Line(500m)],
            new PricingContext(15m, true, Discount: Money.Taka(900m)));

        totals.DiscountTotal.Amount.ShouldBe(500m);
        totals.GrandTotal.Amount.ShouldBe(0m);
        totals.GrandTotal.IsNegative.ShouldBeFalse();
    }

    [Fact]
    public void A_negative_discount_is_ignored_rather_than_charged()
    {
        // Belt and braces against a malformed promotion: a negative discount is
        // a surcharge nobody agreed to.
        var totals = CartPricer.Price(
            [Line(1000m)],
            new PricingContext(0m, true, Discount: Money.Taka(-250m)));

        totals.DiscountTotal.Amount.ShouldBe(0m);
        totals.GrandTotal.Amount.ShouldBe(1000m);
    }

    // -------------------------------------------------------------------------
    // Rounding
    // -------------------------------------------------------------------------

    [Fact]
    public void Awkward_prices_do_not_drift_across_many_lines()
    {
        var lines = Enumerable.Range(0, 100).Select(_ => Line(0.10m)).ToList();

        var totals = CartPricer.Price(lines, new PricingContext(0m, true));

        totals.Subtotal.Amount.ShouldBe(10.00m);
    }

    [Fact]
    public void Vat_is_rounded_to_two_places()
    {
        // 1,234.56 × 15/115 = 161.0295652…
        var totals = CartPricer.Price([Line(1234.56m)], new PricingContext(15m, true));

        totals.VatAmount.Amount.ShouldBe(161.03m);
        totals.GoodsNet.Amount.ShouldBe(1073.53m);
        (totals.GoodsNet + totals.VatAmount).Amount.ShouldBe(1234.56m);
    }
}
