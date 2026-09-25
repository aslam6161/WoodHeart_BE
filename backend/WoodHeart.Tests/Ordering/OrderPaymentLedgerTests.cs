using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Tests.Ordering;

/// <summary>
/// What the shop has actually taken, as opposed to where the money stands.
/// </summary>
/// <remarks>
/// <para>
/// <c>PaymentStatus</c> is one word for a thing that can have happened several
/// times. An advance, then the balance at the door, then half of it back the
/// following week is three events and one word, and only the ledger can be
/// asked which of them happened when.
/// </para>
/// <para>
/// The totals are computed from the rows rather than stored beside them,
/// because a stored total is a number that can disagree with its own history,
/// and on a shop whose takings are mostly cash the history is all there is.
/// </para>
/// </remarks>
public class OrderPaymentLedgerTests
{
    [Fact]
    public void An_order_nobody_has_paid_owes_all_of_it()
    {
        var order = Order(15_250m);

        order.AmountPaid.Amount.ShouldBe(0m);
        order.AmountOutstanding.Amount.ShouldBe(15_250m);
    }

    [Fact]
    public void An_advance_leaves_the_rest_outstanding()
    {
        var order = Order(15_250m);

        order.Payments.Add(Received(5_000m));

        order.AmountPaid.Amount.ShouldBe(5_000m);
        order.AmountOutstanding.Amount.ShouldBe(10_250m);
    }

    [Fact]
    public void Money_arriving_twice_adds_up()
    {
        // The ordinary shape of a made-to-order sale here: something down when
        // the work is agreed, the rest when it arrives.
        var order = Order(15_250m);

        order.Payments.Add(Received(5_000m));
        order.Payments.Add(Received(10_250m));

        order.AmountPaid.Amount.ShouldBe(15_250m);
        order.AmountOutstanding.Amount.ShouldBe(0m);
    }

    [Fact]
    public void Money_going_back_comes_off_again()
    {
        var order = Order(15_250m);

        order.Payments.Add(Received(15_250m));
        order.Payments.Add(Refunded(5_000m));

        order.AmountPaid.Amount.ShouldBe(10_250m);
        order.AmountOutstanding.Amount.ShouldBe(5_000m);
    }

    [Fact]
    public void A_full_refund_leaves_the_shop_holding_nothing()
    {
        var order = Order(15_250m);

        order.Payments.Add(Received(15_250m));
        order.Payments.Add(Refunded(15_250m));

        order.AmountPaid.Amount.ShouldBe(0m);
        order.AmountOutstanding.Amount.ShouldBe(15_250m);
    }

    [Fact]
    public void An_overpayment_is_not_turned_into_a_debt()
    {
        // A rider who took a round 16,000 for a 15,250 order and has not yet
        // handed back the change. The shop owes the customer; the customer does
        // not owe a negative amount, and a screen reading "-750 outstanding"
        // is a screen somebody will try to collect on.
        var order = Order(15_250m);

        order.Payments.Add(Received(16_000m));

        order.AmountPaid.Amount.ShouldBe(16_000m);
        order.AmountOutstanding.Amount.ShouldBe(0m);
    }

    [Fact]
    public void The_ledger_keeps_what_the_status_cannot()
    {
        // The whole argument for the table. One word cannot hold three events,
        // and "who took it, when, and against what reference" is what somebody
        // needs when a customer disputes a payment a month later.
        var order = Order(15_250m);

        order.Payments.Add(new OrderPayment
        {
            Direction = PaymentDirection.Received,
            Amount = Money.Taka(5_000m),
            MethodCode = "bkash",
            Reference = "TrxID 8HG23K",
            ActorName = "Rakib (admin)",
            OccurredAt = new DateTimeOffset(2026, 9, 20, 6, 0, 0, TimeSpan.Zero)
        });

        order.Payments.Add(new OrderPayment
        {
            Direction = PaymentDirection.Received,
            Amount = Money.Taka(10_250m),
            MethodCode = "cod",
            Reference = "Rider: Jamal",
            ActorName = "System",
            OccurredAt = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero)
        });

        order.PaymentStatus = PaymentStatus.Paid;

        // One status, two events, two ways the money came, two people
        // answerable for it.
        order.Payments.Count.ShouldBe(2);
        order.Payments.Select(p => p.MethodCode).ShouldBe(["bkash", "cod"]);
        order.Payments.Select(p => p.Reference).ShouldBe(["TrxID 8HG23K", "Rider: Jamal"]);
    }

    private static Order Order(decimal grandTotal) =>
        new()
        {
            Id = 1,
            OrderNumber = "WH-2609-00042",
            Currency = Money.Bdt,
            GrandTotal = Money.Taka(grandTotal),
            PaymentMethodCode = "cod"
        };

    private static OrderPayment Received(decimal amount) =>
        new()
        {
            Direction = PaymentDirection.Received,
            Amount = Money.Taka(amount),
            MethodCode = "cod",
            ActorName = "Rakib (admin)"
        };

    private static OrderPayment Refunded(decimal amount) =>
        new()
        {
            Direction = PaymentDirection.Refunded,
            Amount = Money.Taka(amount),
            MethodCode = "cod",
            ActorName = "Rakib (admin)"
        };
}
