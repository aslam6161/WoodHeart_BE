using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.Ordering;

namespace WoodHeart.Tests.Ordering;

/// <summary>
/// Which order-status moves are legal.
/// </summary>
/// <remarks>
/// <para>
/// The transitions are a table rather than scattered <c>if</c>s so that the
/// admin screen, the API and these tests cannot disagree about whether an order
/// can still be cancelled. What follows is that table read back as the
/// behaviour a shop would recognise.
/// </para>
/// <para>
/// The rules with money behind them, and therefore the most heavily tested:
/// <b>cancellation stops when the van leaves</b>, and <b>nothing ever moves
/// backwards</b>.
/// </para>
/// </remarks>
public class OrderStatusMachineTests
{
    // -------------------------------------------------------------------------
    // The happy path, one step at a time
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Processing)]
    [InlineData(OrderStatus.Processing, OrderStatus.ReadyToShip)]
    [InlineData(OrderStatus.ReadyToShip, OrderStatus.Shipped)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Delivered)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Completed)]
    public void The_ordinary_life_of_an_order_is_permitted(OrderStatus from, OrderStatus to) =>
        OrderStatusMachine.CanTransition(from, to).ShouldBeTrue($"{from} -> {to} should be allowed");

    [Fact]
    public void Steps_cannot_be_skipped()
    {
        // Marking a confirmed order Shipped without it ever being picked or
        // packed loses the only record that the work happened.
        OrderStatusMachine.CanTransition(OrderStatus.Confirmed, OrderStatus.Shipped).ShouldBeFalse();
        OrderStatusMachine.CanTransition(OrderStatus.Pending, OrderStatus.Delivered).ShouldBeFalse();
    }

    [Theory]
    [InlineData(OrderStatus.Shipped, OrderStatus.Processing)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Shipped)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Pending)]
    [InlineData(OrderStatus.Completed, OrderStatus.Delivered)]
    public void Nothing_moves_backwards(OrderStatus from, OrderStatus to)
    {
        // Shipped -> Processing looks harmless and is not: the customer has had
        // the "on its way" message. A mistake is corrected by cancelling, which
        // leaves a record of what happened.
        OrderStatusMachine.CanTransition(from, to).ShouldBeFalse($"{from} -> {to} must not be allowed");
    }

    [Fact]
    public void A_status_cannot_transition_to_itself()
    {
        foreach (var status in Enum.GetValues<OrderStatus>())
        {
            OrderStatusMachine.CanTransition(status, status)
                .ShouldBeFalse($"{status} -> {status} would write a timeline entry saying nothing");
        }
    }

    // -------------------------------------------------------------------------
    // Cancellation — the rule with a van behind it
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Processing)]
    [InlineData(OrderStatus.ReadyToShip)]
    public void An_order_can_be_cancelled_right_up_until_it_leaves(OrderStatus from) =>
        OrderStatusMachine.CanTransition(from, OrderStatus.Cancelled)
            .ShouldBeTrue($"{from} should still be cancellable");

    [Theory]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Completed)]
    public void Once_it_has_left_it_is_a_return_rather_than_a_cancellation(OrderStatus from)
    {
        // The distinction is not pedantry. A cancelled order cost nothing; a
        // returned one has already paid for a trip out and a trip back, and
        // collapsing the two hides that cost.
        OrderStatusMachine.CanTransition(from, OrderStatus.Cancelled).ShouldBeFalse();
        OrderStatusMachine.CanTransition(from, OrderStatus.Returned).ShouldBeTrue();
    }

    [Fact]
    public void A_customer_may_stop_their_own_order_only_before_work_starts()
    {
        OrderStatusMachine.IsCustomerCancellable(OrderStatus.Pending).ShouldBeTrue();
        OrderStatusMachine.IsCustomerCancellable(OrderStatus.Confirmed).ShouldBeTrue();

        // Staff can still cancel a wardrobe that is half built — but at that
        // point it is a conversation about a deposit, not a button on a page.
        OrderStatusMachine.IsCustomerCancellable(OrderStatus.Processing).ShouldBeFalse();
        OrderStatusMachine.CanTransition(OrderStatus.Processing, OrderStatus.Cancelled).ShouldBeTrue();
    }

    // -------------------------------------------------------------------------
    // Ends
    // -------------------------------------------------------------------------

    [Fact]
    public void Cancelled_and_refunded_are_the_only_dead_ends()
    {
        OrderStatusMachine.IsTerminal(OrderStatus.Cancelled).ShouldBeTrue();
        OrderStatusMachine.IsTerminal(OrderStatus.Refunded).ShouldBeTrue();

        // Completed is not terminal: a return can still arrive inside the
        // window, and an order that could not accept one would force staff to
        // record the refund somewhere else.
        OrderStatusMachine.IsTerminal(OrderStatus.Completed).ShouldBeFalse();
    }

    [Fact]
    public void A_return_can_only_be_followed_by_a_refund()
    {
        OrderStatusMachine.NextFrom(OrderStatus.Returned).ShouldBe([OrderStatus.Refunded]);
    }

    [Fact]
    public void Every_status_is_reachable_from_Pending()
    {
        // A status nothing can reach is dead code that still shows up in the
        // admin filter dropdown, and staff spend a morning wondering why it is
        // always empty.
        var reached = new HashSet<OrderStatus> { OrderStatus.Pending };
        var frontier = new Queue<OrderStatus>([OrderStatus.Pending]);

        while (frontier.Count > 0)
        {
            foreach (var next in OrderStatusMachine.NextFrom(frontier.Dequeue()))
            {
                if (reached.Add(next))
                {
                    frontier.Enqueue(next);
                }
            }
        }

        reached.ShouldBe(Enum.GetValues<OrderStatus>(), ignoreOrder: true);
    }

    [Fact]
    public void Every_status_has_an_entry_in_the_table()
    {
        // A status missing from the table silently allows nothing, so an order
        // that reached it could never move again — and the bug would present as
        // "the buttons have disappeared" long after the release that caused it.
        foreach (var status in Enum.GetValues<OrderStatus>())
        {
            var next = OrderStatusMachine.NextFrom(status);

            if (status is OrderStatus.Cancelled or OrderStatus.Refunded)
            {
                next.ShouldBeEmpty();
            }
            else
            {
                next.ShouldNotBeEmpty($"{status} has no legal move out of it");
            }
        }
    }
}
