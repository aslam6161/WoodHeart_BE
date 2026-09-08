using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Service.Mapping.Ordering;

namespace WoodHeart.Tests.Ordering;

/// <summary>
/// What the invoice says.
/// </summary>
/// <remarks>
/// <para>
/// The figures are the part of an invoice worth testing; the millimetres are
/// not. Two things here carry real consequences: <b>nothing is recomputed from
/// the lines</b>, and <b>"amount due" appears only when the whole amount is
/// genuinely still owed</b> — a rider reads that box and collects what it says.
/// </para>
/// </remarks>
public class InvoiceMapperTests
{
    [Fact]
    public void The_invoice_number_is_the_order_number()
    {
        // One number for the order and its invoice. A second sequence would
        // mean staff holding two numbers for the same thing.
        InvoiceFixture.Model().InvoiceNumber.ShouldBe(InvoiceFixture.OrderNumber);
    }

    [Fact]
    public void Every_figure_is_copied_off_the_order_rather_than_recomputed()
    {
        // The case that matters: staff overrode the delivery charge, so the
        // lines' own charges no longer add up to what was billed. An invoice
        // that re-derived the total would disagree with the order, the SMS the
        // customer already has, and the till.
        var order = InvoiceFixture.Order();
        order.DeliveryFee = Money.Taka(1_600m);
        order.DeliveryOverridden = true;
        order.GrandTotal = Money.Taka(12_350m);

        var totals = InvoiceFixture.Model(order).Totals;

        totals.DeliveryFee.ShouldBe(1_600m);
        totals.GrandTotal.ShouldBe(12_350m);

        // And the lines still say what the rate card said, untouched.
        order.Lines.Sum(line => line.DeliveryChargeApplied.Amount).ShouldBe(3_000m);
    }

    [Fact]
    public void The_vat_rate_and_regime_come_from_the_order_not_from_settings()
    {
        // An invoice reprinted after the rate changes must still show what the
        // customer actually paid.
        var totals = InvoiceFixture.Model().Totals;

        totals.VatRatePercent.ShouldBe(7.5m);
        totals.VatAmount.ShouldBe(750m);
        totals.PricesIncludeVat.ShouldBeTrue();
    }

    [Theory]
    [InlineData(PaymentStatus.Unpaid, true)]
    [InlineData(PaymentStatus.Failed, true)]
    [InlineData(PaymentStatus.AdvancePaid, false)]
    [InlineData(PaymentStatus.Paid, false)]
    [InlineData(PaymentStatus.PartiallyRefunded, false)]
    [InlineData(PaymentStatus.Refunded, false)]
    public void Amount_due_is_printed_only_when_the_whole_amount_is_still_owed(
        PaymentStatus status, bool expected)
    {
        // AdvancePaid is the one worth stating plainly. The order records that
        // a deposit arrived but not how much, so printing the grand total as
        // "due" would have the rider collect the deposit a second time.
        var payment = InvoiceFixture.Model(InvoiceFixture.Order(status)).Payment;

        (payment.AmountDue is not null).ShouldBe(expected);

        if (expected)
        {
            payment.AmountDue.ShouldBe(15_250m);
        }
    }

    [Fact]
    public void The_payment_status_is_spelled_for_a_reader()
    {
        // "AdvancePaid" is an enum name. A customer holding the paper reads
        // English.
        InvoiceFixture.Model(InvoiceFixture.Order(PaymentStatus.AdvancePaid))
            .Payment.StatusName.ShouldBe("Advance received");

        InvoiceFixture.Model(InvoiceFixture.Order(PaymentStatus.Paid))
            .Payment.StatusName.ShouldBe("Paid in full");
    }

    [Fact]
    public void Lines_keep_the_order_they_were_added_to_the_basket_in()
    {
        // So the invoice can be checked against the packing list beside it
        // without either being re-sorted in somebody's head.
        var order = InvoiceFixture.Order(lines:
        [
            InvoiceFixture.Line(9, "Wardrobe", 1, 30_000m),
            InvoiceFixture.Line(3, "Dining Table", 1, 22_000m)
        ]);

        InvoiceFixture.Model(order).Lines
            .Select(line => line.Description)
            .ShouldBe(["Dining Table", "Wardrobe"]);
    }

    // -------------------------------------------------------------------------
    // The address
    // -------------------------------------------------------------------------

    [Fact]
    public void The_address_reads_narrowest_first_down_the_page()
    {
        var address = DeliveryAddress.Create(
            "Dhaka", "Dhaka", "House 12, Road 3",
            upazila: "Dhanmondi", area: "Jigatola",
            landmark: "Opposite Popular Diagnostic", postcode: "1209");

        InvoiceMapper.AddressLines(address).ShouldBe(
        [
            "House 12, Road 3",
            "Opposite Popular Diagnostic",
            "Jigatola, Dhanmondi",
            "Dhaka, Dhaka – 1209"
        ]);
    }

    [Fact]
    public void An_address_with_nothing_optional_on_it_produces_no_blank_lines()
    {
        // A blank line on a delivery label reads as a missing address, and the
        // rider rings the shop about it.
        var address = DeliveryAddress.Create("Sylhet", "Sylhet", "Village Road, Beanibazar");

        InvoiceMapper.AddressLines(address).ShouldBe(
        [
            "Village Road, Beanibazar",
            "Sylhet, Sylhet"
        ]);
    }

    [Fact]
    public void A_Bangla_address_is_carried_through_exactly_as_the_customer_typed_it()
    {
        // Not transliterated, not dropped. The rider reads this one.
        var address = DeliveryAddress.Create(
            "ঢাকা", "ঢাকা", "বাড়ি ১২, রোড ৩", area: "ধানমন্ডি");

        InvoiceMapper.AddressLines(address).ShouldBe(
        [
            "বাড়ি ১২, রোড ৩",
            "ধানমন্ডি",
            "ঢাকা, ঢাকা"
        ]);
    }
}
