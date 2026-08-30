namespace WoodHeart.Domain.Enums.Payments;

/// <summary>Where a payment attempt currently stands at the provider.</summary>
/// <remarks>
/// Deliberately separate from order status. A cash-on-delivery order is
/// Confirmed and Unpaid for its entire life until the rider collects, and
/// collapsing the two into one status field is what makes "confirmed but not
/// yet paid" impossible to express.
/// </remarks>
public enum PaymentState
{
    /// <summary>Created at the gateway; waiting for the customer to act.</summary>
    Pending = 0,

    /// <summary>Money confirmed received.</summary>
    Succeeded = 1,

    /// <summary>The gateway rejected it, or the customer abandoned it.</summary>
    Failed = 2,

    /// <summary>The customer cancelled deliberately.</summary>
    Cancelled = 3,

    /// <summary>
    /// Accepted, but the money is not in hand. Cash on delivery lives here for
    /// its entire life until the rider collects.
    /// </summary>
    AwaitingCollection = 4,

    Refunded = 5,

    PartiallyRefunded = 6
}
