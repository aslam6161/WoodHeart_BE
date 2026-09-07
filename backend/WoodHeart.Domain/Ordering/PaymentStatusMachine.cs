using WoodHeart.Domain.Enums.Ordering;

namespace WoodHeart.Domain.Ordering;

/// <summary>
/// Which moves along the money axis are legal.
/// </summary>
/// <remarks>
/// <para>
/// The sibling of <see cref="OrderStatusMachine"/>, and separate from it for
/// the reason the two enums are separate: a cash-on-delivery order sits at
/// <see cref="OrderStatus.Confirmed"/> and <see cref="PaymentStatus.Unpaid"/>
/// for its whole life, and the two axes reach their end states at different
/// moments and for different reasons.
/// </para>
/// <para>
/// <b>Refunded is terminal and Paid cannot be undone.</b> Money that has been
/// sent back is a fact about a bank, not a field; an operator who marks an
/// order Refunded by mistake needs a correcting entry and somebody's attention,
/// not an Undo button that makes the ledger disagree with the account.
/// </para>
/// <para>
/// Failed can go back to Unpaid, because a declined card that is retried
/// successfully is the ordinary case and not an anomaly.
/// </para>
/// </remarks>
public static class PaymentStatusMachine
{
    private static readonly IReadOnlyDictionary<PaymentStatus, PaymentStatus[]> Allowed =
        new Dictionary<PaymentStatus, PaymentStatus[]>
        {
            [PaymentStatus.Unpaid] =
                [PaymentStatus.AdvancePaid, PaymentStatus.Paid, PaymentStatus.Failed],

            // The balance arrives, or the customer walks away from a deposit
            // they do not get back in full.
            [PaymentStatus.AdvancePaid] =
                [PaymentStatus.Paid, PaymentStatus.PartiallyRefunded, PaymentStatus.Refunded],

            [PaymentStatus.Paid] = [PaymentStatus.PartiallyRefunded, PaymentStatus.Refunded],

            [PaymentStatus.PartiallyRefunded] = [PaymentStatus.Refunded],

            // A retry that works. Without this a customer whose first card was
            // declined could never pay at all.
            [PaymentStatus.Failed] = [PaymentStatus.Unpaid, PaymentStatus.Paid],

            [PaymentStatus.Refunded] = []
        };

    public static bool CanTransition(PaymentStatus from, PaymentStatus to) =>
        Allowed.TryGetValue(from, out var next) && Array.IndexOf(next, to) >= 0;

    /// <summary>Every move legal from here, for the admin screen to render.</summary>
    public static IReadOnlyList<PaymentStatus> NextFrom(PaymentStatus status) =>
        Allowed.TryGetValue(status, out var next) ? next : [];

    /// <summary>
    /// Whether the order's total may still be edited.
    /// </summary>
    /// <remarks>
    /// Once anything has been collected, changing the total silently makes the
    /// books disagree with what was actually taken. The correction after that
    /// point is a refund or a second collection, both of which leave a record.
    /// </remarks>
    public static bool IsAmountStillEditable(PaymentStatus status) =>
        status is PaymentStatus.Unpaid or PaymentStatus.Failed;
}
