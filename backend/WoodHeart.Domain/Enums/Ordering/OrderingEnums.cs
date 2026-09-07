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
