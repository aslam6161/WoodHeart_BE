using WoodHeart.Domain.Enums.Ordering;
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
/// <para>
/// Delivery has its own suite — see <c>DeliveryPricerTests</c>. What is tested
/// here is only how the delivery figure interacts with tax and the total.
/// </para>
/// </remarks>
public class CartPricerTests
{
    private static PricedLine Line(decimal unitPrice, int quantity = 1) =>
        new(quantity, Money.Taka(unitPrice));

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
        // A shop that is not VAT-registered sets the rate to zero, and the
        // bill must then be identical either way round.
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
                Zone: DeliveryZone.InsideDhaka,
                DefaultDeliveryCharge: Money.Taka(150m)));

        var sum = totals.GoodsNet + totals.VatAmount + totals.DeliveryFee;

        sum.Amount.ShouldBe(totals.GrandTotal.Amount);
    }

    [Fact]
    public void Delivery_is_untaxed_unless_the_setting_says_otherwise()
    {
        var lines = new[] { Line(1000m) };
        var withoutTax = new PricingContext(15m, false, Zone: DeliveryZone.InsideDhaka,
                DefaultDeliveryCharge: Money.Taka(200m));
        var withTax = withoutTax with { VatOnDelivery = true };

        CartPricer.Price(lines, withoutTax).VatAmount.Amount.ShouldBe(150m);

        // 15% of 1,200 rather than of 1,000.
        CartPricer.Price(lines, withTax).VatAmount.Amount.ShouldBe(180m);
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
