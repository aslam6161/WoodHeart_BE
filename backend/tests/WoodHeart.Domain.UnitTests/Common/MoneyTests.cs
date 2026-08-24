using WoodHeart.Domain.Common;

namespace WoodHeart.Domain.UnitTests.Common;

/// <summary>
/// Money is the type most likely to lose the business real taka if it is wrong,
/// so it gets the most thorough tests in the Domain.
/// </summary>
public class MoneyTests
{
    [Fact]
    public void Taka_creates_a_bdt_amount()
    {
        var money = Money.Taka(1500m);

        money.Amount.ShouldBe(1500m);
        money.Currency.ShouldBe("BDT");
    }

    [Fact]
    public void Amounts_round_to_two_decimal_places_on_construction()
    {
        // Stored value must equal what the numeric(18,2) column holds, or code
        // and database will disagree about the same order total.
        Money.Taka(10.005m).Amount.ShouldBe(10.00m);
        Money.Taka(10.015m).Amount.ShouldBe(10.02m);
        Money.Taka(10.006m).Amount.ShouldBe(10.01m);
    }

    [Fact]
    public void Repeated_addition_does_not_drift()
    {
        // The exact scenario decimal exists to prevent: this loop returns
        // 100.00000000000002 with double, and a report that is off by paisa.
        var total = Money.Zero();

        for (var i = 0; i < 1_000; i++)
        {
            total += Money.Taka(0.10m);
        }

        total.Amount.ShouldBe(100.00m);
    }

    [Fact]
    public void Mixing_currencies_throws_rather_than_producing_a_wrong_number()
    {
        var taka = Money.Taka(100m);
        var dollars = Money.From(100m, "USD");

        // The failure mode this prevents: silently returning 200 of something.
        Should.Throw<InvalidOperationException>(() => taka + dollars);
        Should.Throw<InvalidOperationException>(() => taka - dollars);
        Should.Throw<InvalidOperationException>(() => taka > dollars);
    }

    [Fact]
    public void Percentage_computes_vat_correctly()
    {
        var subtotal = Money.Taka(2000m);

        subtotal.Percentage(15m).Amount.ShouldBe(300.00m);
        subtotal.Percentage(7.5m).Amount.ShouldBe(150.00m);
    }

    [Fact]
    public void CapAt_limits_a_percentage_discount()
    {
        // "20% off, up to 500 taka maximum" — without the cap, a 50,000 taka
        // bedroom set would take a 10,000 taka discount.
        var discount = Money.Taka(50_000m).Percentage(20m);

        discount.Amount.ShouldBe(10_000m);
        discount.CapAt(Money.Taka(500m)).Amount.ShouldBe(500m);
    }

    [Fact]
    public void OrZeroIfNegative_prevents_a_discount_exceeding_the_line_total()
    {
        var lineTotal = Money.Taka(300m);
        var oversizedDiscount = Money.Taka(500m);

        (lineTotal - oversizedDiscount).OrZeroIfNegative().IsZero.ShouldBeTrue();
    }

    [Fact]
    public void RoundToWholeTaka_produces_an_amount_a_rider_can_collect()
    {
        // Cash on delivery: the customer hands over notes, not paisa.
        Money.Taka(1499.49m).RoundToWholeTaka().Amount.ShouldBe(1499m);
        Money.Taka(1499.50m).RoundToWholeTaka().Amount.ShouldBe(1500m);
    }

    [Fact]
    public void Equality_compares_amount_and_currency_not_reference()
    {
        Money.Taka(500m).ShouldBe(Money.Taka(500m));
        Money.Taka(500m).ShouldNotBe(Money.Taka(501m));
        Money.Taka(500m).ShouldNotBe(Money.From(500m, "USD"));
    }

    [Fact]
    public void Comparison_operators_order_amounts()
    {
        Money.Taka(500m).ShouldBeGreaterThan(Money.Taka(400m));
        Money.Taka(500m).ShouldBeLessThan(Money.Taka(600m));
        (Money.Taka(500m) >= Money.Taka(500m)).ShouldBeTrue();
    }

    [Fact]
    public void CompareTo_gives_a_total_order_without_throwing()
    {
        // IComparable is called by OrderBy, Comparer<T>.Default and several test
        // frameworks on values they know nothing about. Throwing there would turn
        // an innocent sort into a crash, so mixed currencies group rather than fail.
        var mixed = new[]
        {
            Money.From(300m, "USD"),
            Money.Taka(500m),
            Money.From(100m, "USD"),
            Money.Taka(100m)
        };

        var sorted = mixed.OrderBy(m => m).ToArray();

        sorted[0].ShouldBe(Money.Taka(100m));
        sorted[1].ShouldBe(Money.Taka(500m));
        sorted[2].ShouldBe(Money.From(100m, "USD"));
        sorted[3].ShouldBe(Money.From(300m, "USD"));
    }

    [Fact]
    public void Relational_operators_still_refuse_to_compare_currencies()
    {
        // The total order above must not weaken the business guard: asking
        // whether 500 BDT exceeds 400 USD is a bug, not a question.
        Should.Throw<InvalidOperationException>(() => Money.Taka(500m) > Money.From(400m, "USD"));
    }

    [Fact]
    public void Currency_must_be_a_three_letter_code()
    {
        Should.Throw<ArgumentException>(() => Money.From(10m, "TAKA"));
        Should.Throw<ArgumentException>(() => Money.From(10m, ""));
    }

    [Fact]
    public void Currency_code_is_normalised_to_uppercase()
    {
        Money.From(10m, "bdt").Currency.ShouldBe("BDT");
    }

    [Fact]
    public void Dividing_by_zero_throws()
    {
        Should.Throw<DivideByZeroException>(() => Money.Taka(100m).Divide(0m));
    }
}
