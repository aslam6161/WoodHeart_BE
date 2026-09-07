using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.Ordering;

namespace WoodHeart.Tests.Ordering;

/// <summary>
/// Which moves along the money axis are legal.
/// </summary>
/// <remarks>
/// The rules with money behind them: <b>a refund is the end</b>, and <b>the
/// total stops being editable the moment anything has been collected</b>. Both
/// are the sort of thing that looks like an inconvenience until the shop's
/// books disagree with its bank statement.
/// </remarks>
public class PaymentStatusMachineTests
{
    [Fact]
    public void An_unpaid_order_can_be_paid() =>
        PaymentStatusMachine.CanTransition(PaymentStatus.Unpaid, PaymentStatus.Paid)
            .ShouldBeTrue();

    [Fact]
    public void A_deposit_can_be_followed_by_the_balance() =>
        PaymentStatusMachine.CanTransition(PaymentStatus.AdvancePaid, PaymentStatus.Paid)
            .ShouldBeTrue();

    [Fact]
    public void A_declined_card_can_be_retried_successfully()
    {
        // The ordinary case, not an anomaly. Without this a customer whose
        // first attempt failed could never pay at all.
        PaymentStatusMachine.CanTransition(PaymentStatus.Failed, PaymentStatus.Paid)
            .ShouldBeTrue();

        PaymentStatusMachine.CanTransition(PaymentStatus.Failed, PaymentStatus.Unpaid)
            .ShouldBeTrue();
    }

    [Fact]
    public void A_refund_is_the_end_of_it()
    {
        // Money that has gone back is a fact about a bank, not a field. An undo
        // here would let the order disagree with the account.
        PaymentStatusMachine.NextFrom(PaymentStatus.Refunded).ShouldBeEmpty();

        PaymentStatusMachine.CanTransition(PaymentStatus.Refunded, PaymentStatus.Paid)
            .ShouldBeFalse();
    }

    [Fact]
    public void A_paid_order_cannot_quietly_become_unpaid()
    {
        // The correction from here is a refund, which leaves a record, rather
        // than a status change, which does not.
        PaymentStatusMachine.CanTransition(PaymentStatus.Paid, PaymentStatus.Unpaid)
            .ShouldBeFalse();
    }

    [Theory]
    [InlineData(PaymentStatus.Unpaid, true)]
    [InlineData(PaymentStatus.Failed, true)]
    [InlineData(PaymentStatus.AdvancePaid, false)]
    [InlineData(PaymentStatus.Paid, false)]
    [InlineData(PaymentStatus.PartiallyRefunded, false)]
    [InlineData(PaymentStatus.Refunded, false)]
    public void The_total_stops_being_editable_once_anything_is_collected(
        PaymentStatus status, bool editable)
    {
        // A deposit counts. Somebody has handed over money against a figure,
        // and moving the figure afterwards is how a customer ends up owing an
        // amount nobody quoted them.
        PaymentStatusMachine.IsAmountStillEditable(status).ShouldBe(editable);
    }

    [Fact]
    public void Every_status_except_the_terminal_one_can_go_somewhere()
    {
        foreach (var status in Enum.GetValues<PaymentStatus>())
        {
            if (status == PaymentStatus.Refunded)
            {
                continue;
            }

            PaymentStatusMachine.NextFrom(status)
                .ShouldNotBeEmpty($"{status} is a dead end that is not meant to be one");
        }
    }
}
