using WoodHeart.Domain.Enums.Ordering;

namespace WoodHeart.Domain.Ordering;

/// <summary>
/// Which order-status moves are legal, and which are not.
/// </summary>
/// <remarks>
/// <para>
/// A pure lookup with no database, no clock and no permissions in it, for the
/// same reason <c>CartPricer</c> is pure: the admin screen that greys out a
/// button, the API that rejects a request, and the tests that prove it all read
/// from this one table. Three copies of "can this be cancelled?" is three
/// answers, and the one the customer gets is whichever screen they happened to
/// be looking at.
/// </para>
/// <para>
/// <b>Permission is a separate question.</b> This says a move is possible;
/// whether <i>this caller</i> may make it belongs to the service. A customer
/// cancelling their own pending order and a manager cancelling a shipped one
/// are different rights over the same transition.
/// </para>
/// <para>
/// <b>Backwards moves are absent deliberately.</b> Shipped → Processing looks
/// harmless and is not: the customer has had the "on its way" SMS, and a status
/// that can go backwards makes the timeline unreadable as a history. A mistake
/// is corrected by cancelling and re-placing, which leaves a record of what
/// happened.
/// </para>
/// </remarks>
public static class OrderStatusMachine
{
    /// <summary>
    /// The graph, as adjacency. Everything else here reads from it.
    /// </summary>
    private static readonly IReadOnlyDictionary<OrderStatus, OrderStatus[]> Allowed =
        new Dictionary<OrderStatus, OrderStatus[]>
        {
            [OrderStatus.Pending] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
            [OrderStatus.Confirmed] = [OrderStatus.Processing, OrderStatus.Cancelled],

            // Cancellable up to the point it leaves the building. After that the
            // van is out and it becomes a return, which is a different piece of
            // work with a different cost.
            [OrderStatus.Processing] = [OrderStatus.ReadyToShip, OrderStatus.Cancelled],
            [OrderStatus.ReadyToShip] = [OrderStatus.Shipped, OrderStatus.Cancelled],

            // No Cancelled from here. A refused delivery comes back as Returned,
            // so the trip that was already paid for is still visible.
            [OrderStatus.Shipped] = [OrderStatus.Delivered, OrderStatus.Returned],

            [OrderStatus.Delivered] = [OrderStatus.Completed, OrderStatus.Returned],
            [OrderStatus.Completed] = [OrderStatus.Returned],
            [OrderStatus.Returned] = [OrderStatus.Refunded],

            // Terminal.
            [OrderStatus.Cancelled] = [],
            [OrderStatus.Refunded] = []
        };

    /// <summary>Statuses from which nothing further can happen.</summary>
    public static bool IsTerminal(OrderStatus status) =>
        Allowed.TryGetValue(status, out var next) && next.Length == 0;

    public static bool CanTransition(OrderStatus from, OrderStatus to) =>
        Allowed.TryGetValue(from, out var next) && Array.IndexOf(next, to) >= 0;

    /// <summary>Every move legal from here, for an admin screen to render as buttons.</summary>
    public static IReadOnlyList<OrderStatus> NextFrom(OrderStatus status) =>
        Allowed.TryGetValue(status, out var next) ? next : [];

    /// <summary>
    /// Whether the customer may still stop the order themselves.
    /// </summary>
    /// <remarks>
    /// Narrower than what staff can cancel, and deliberately so. Once the shop
    /// has started building a made-to-order wardrobe, stopping it is a
    /// conversation about a deposit rather than a button — so a customer's own
    /// cancellation ends at <see cref="OrderStatus.Confirmed"/>.
    /// </remarks>
    public static bool IsCustomerCancellable(OrderStatus status) =>
        status is OrderStatus.Pending or OrderStatus.Confirmed;
}
