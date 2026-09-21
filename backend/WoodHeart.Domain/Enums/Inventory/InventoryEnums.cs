namespace WoodHeart.Domain.Enums.Inventory;

/// <summary>
/// Why a stock number moved. Every change to <c>OnHand</c> has one of these.
/// </summary>
public enum StockMovementType
{
    /// <summary>Goods received from a supplier or the workshop. Adds.</summary>
    Purchase = 0,

    /// <summary>Goods that left with an order. Deducts, and settles the reservation.</summary>
    Sale = 1,

    /// <summary>Goods that came back from a customer. Adds.</summary>
    Return = 2,

    /// <summary>A count correction, either way. "The system said 5, we have 3."</summary>
    Adjustment = 3,

    /// <summary>Written off — broken, water-damaged, sold as scrap. Deducts.</summary>
    Damage = 4,

    /// <summary>Moved in from another location. Adds.</summary>
    TransferIn = 5,

    /// <summary>Moved out to another location. Deducts.</summary>
    TransferOut = 6
}

/// <summary>Where a reservation is in its life.</summary>
public enum StockReservationStatus
{
    /// <summary>Holding units against an order that has not shipped.</summary>
    Active = 0,

    /// <summary>The order shipped; the units left <c>OnHand</c> with a Sale movement.</summary>
    Committed = 1,

    /// <summary>The order was cancelled before shipping; the units are available again.</summary>
    Released = 2
}
