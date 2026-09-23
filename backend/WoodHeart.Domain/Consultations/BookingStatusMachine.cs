using WoodHeart.Domain.Enums.Consultations;

namespace WoodHeart.Domain.Consultations;

/// <summary>
/// Which booking-status moves are legal, and which are not.
/// </summary>
/// <remarks>
/// <para>
/// The same shape and the same reasoning as <c>OrderStatusMachine</c>: one
/// table, read by the admin screen that draws the buttons, by the API that
/// refuses the request, and by the tests. Whether <i>this caller</i> may make a
/// legal move is the service's question, not this one's.
/// </para>
/// <para>
/// <b><see cref="BookingStatus.Rescheduled"/> is a status, not an event.</b> A
/// booking that has been moved is not the same as one that was confirmed at
/// its original time, and a customer who has already had one "we have moved
/// you" message should be able to see that on their own booking. It behaves as
/// Confirmed does — it can be completed, cancelled, moved again or missed.
/// </para>
/// <para>
/// <b>Completed is not terminal.</b> A consultation marked done by mistake is
/// ordinary, and the correction is to put it back to Confirmed rather than to
/// leave the day's figures wrong. Cancelled and NoShow are terminal, because
/// both are the end of that appointment; a customer who wants another one
/// books another one, which is also what the shop wants to be able to count.
/// </para>
/// </remarks>
public static class BookingStatusMachine
{
    private static readonly Dictionary<BookingStatus, BookingStatus[]> Allowed = new()
    {
        [BookingStatus.Requested] =
        [
            BookingStatus.Confirmed,
            BookingStatus.Rescheduled,
            BookingStatus.Cancelled
        ],

        [BookingStatus.Confirmed] =
        [
            BookingStatus.Rescheduled,
            BookingStatus.Completed,
            BookingStatus.Cancelled,
            BookingStatus.NoShow
        ],

        [BookingStatus.Rescheduled] =
        [
            BookingStatus.Confirmed,
            BookingStatus.Rescheduled,
            BookingStatus.Completed,
            BookingStatus.Cancelled,
            BookingStatus.NoShow
        ],

        // Correctable, not terminal: somebody presses Completed on the wrong
        // row, and the figures for the day should not have to stay wrong.
        [BookingStatus.Completed] = [BookingStatus.Confirmed],

        [BookingStatus.Cancelled] = [],
        [BookingStatus.NoShow] = []
    };

    public static bool IsTerminal(BookingStatus status) =>
        Allowed.TryGetValue(status, out var next) && next.Length == 0;

    public static bool CanTransition(BookingStatus from, BookingStatus to) =>
        Allowed.TryGetValue(from, out var next) && Array.IndexOf(next, to) >= 0;

    public static IReadOnlyList<BookingStatus> NextFrom(BookingStatus status) =>
        Allowed.TryGetValue(status, out var next) ? next : [];

    /// <summary>
    /// Whether the slot is still being held.
    /// </summary>
    /// <remarks>
    /// <b>This is the list the unique index is filtered to.</b> A cancelled
    /// booking must not keep an afternoon blocked, and a completed one has
    /// already happened — so only these three occupy a consultant's diary.
    /// Changing this set means changing that index with it, or the database
    /// stops agreeing with the code about what "taken" means.
    /// </remarks>
    public static bool HoldsTheSlot(BookingStatus status) =>
        status is BookingStatus.Requested or BookingStatus.Confirmed or BookingStatus.Rescheduled;

    /// <summary>
    /// Whether the customer may still call it off themselves.
    /// </summary>
    /// <remarks>
    /// Up to the appointment, and no further. Somebody who did not arrive
    /// cannot retroactively cancel, and a consultation already marked done is
    /// a conversation rather than a button.
    /// </remarks>
    public static bool IsCustomerCancellable(BookingStatus status) => HoldsTheSlot(status);
}
