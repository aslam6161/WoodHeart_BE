namespace WoodHeart.Domain.Enums.Ordering;

/// <summary>
/// Where an order is being delivered to, which is what sets the delivery charge.
/// </summary>
/// <remarks>
/// <para>
/// Two zones, not a district table. Every Bangladeshi shop of this size quotes
/// exactly one price inside Dhaka and one price outside it, and a customer
/// reading "delivery inside Dhaka 100৳, outside Dhaka 200৳" understands it
/// immediately. A sixty-four-district rate card is more precise and nobody
/// maintains it.
/// </para>
/// <para>
/// This is stored on the order rather than derived from the address at read
/// time. Redrawing the zone boundary next year must not change what a customer
/// was charged last year.
/// </para>
/// </remarks>
public enum DeliveryZone
{
    /// <summary>Dhaka city and its metropolitan area.</summary>
    InsideDhaka = 0,

    /// <summary>Everywhere else in Bangladesh.</summary>
    OutsideDhaka = 1
}

/// <summary>
/// The life of a cart.
/// </summary>
/// <remarks>
/// A cart is never deleted once it has been checked out — it is the evidence
/// behind an order, and abandoned-cart recovery (Phase 5) needs the abandoned
/// ones to still exist.
/// </remarks>
public enum CartStatus
{
    /// <summary>Being filled. The only status that accepts changes.</summary>
    Active = 0,

    /// <summary>Turned into an order. Frozen; a new cart starts empty.</summary>
    CheckedOut = 1,

    /// <summary>Past its expiry with nothing ordered. Kept for recovery.</summary>
    Abandoned = 2
}

/// <summary>
/// Where an order stands as a piece of work.
/// </summary>
/// <remarks>
/// <para>
/// <b>This axis is about the order, not about the money.</b> Payment lives on
/// <see cref="PaymentStatus"/> and moves independently — a cash-on-delivery
/// order is <see cref="Confirmed"/> and <c>Unpaid</c> for its entire life until
/// the rider collects. Collapsing the two into one field is the classic
/// modelling mistake here, and its cost is precise: "how much cash is out with
/// riders right now" stops being answerable.
/// </para>
/// <para>
/// The permitted moves are in <c>OrderStatusMachine</c> rather than in comments,
/// so that "can this be cancelled?" has one answer that the admin UI, the API
/// and the tests all read from.
/// </para>
/// </remarks>
public enum OrderStatus
{
    /// <summary>Placed, but not yet accepted by the shop.</summary>
    /// <remarks>
    /// Where an online payment sits while the customer is at the gateway. COD
    /// passes straight through it — the provider confirms immediately.
    /// </remarks>
    Pending = 0,

    /// <summary>The shop has accepted it. The customer has been told.</summary>
    Confirmed = 1,

    /// <summary>Being picked, built or finished.</summary>
    Processing = 2,

    /// <summary>Finished and waiting for a vehicle.</summary>
    ReadyToShip = 3,

    /// <summary>With the delivery team.</summary>
    Shipped = 4,

    /// <summary>Handed over. For COD this is also when the money arrives.</summary>
    Delivered = 5,

    /// <summary>Delivered, settled, and past the returns window.</summary>
    Completed = 6,

    /// <summary>Stopped before delivery, by either side.</summary>
    Cancelled = 7,

    /// <summary>Came back after delivery.</summary>
    Returned = 8,

    /// <summary>Returned and the money sent back.</summary>
    Refunded = 9
}

/// <summary>
/// Where the money stands, independently of where the goods are.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Nothing collected. The whole life of a COD order until delivery.</summary>
    Unpaid = 0,

    /// <summary>A deposit taken against a made-to-order item; the balance is due.</summary>
    AdvancePaid = 1,

    Paid = 2,

    PartiallyRefunded = 3,

    Refunded = 4,

    /// <summary>The gateway declined it. The order survives so it can be retried.</summary>
    Failed = 5
}

/// <summary>
/// How far the goods have got, for the warehouse rather than the customer.
/// </summary>
/// <remarks>
/// A third axis because a part-shipped order is real: a bed ready today and its
/// wardrobe in three weeks is one order, one invoice, two vans.
/// </remarks>
public enum FulfilmentStatus
{
    Unfulfilled = 0,

    PartiallyFulfilled = 1,

    Fulfilled = 2,

    Returned = 3
}
