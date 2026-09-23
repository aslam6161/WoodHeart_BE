using WoodHeart.Domain.Enums.Quotations;

namespace WoodHeart.Domain.Quotations;

/// <summary>
/// Which quotation moves are legal, and which are not.
/// </summary>
/// <remarks>
/// <para>
/// The same shape and the same reasoning as <c>OrderStatusMachine</c> and
/// <c>BookingStatusMachine</c>: one table, read by the screen that draws the
/// buttons, by the API that refuses the request, and by the tests. Whether
/// <i>this caller</i> may make a legal move is the service's question, not
/// this one's.
/// </para>
/// <para>
/// <b>A sent quotation can go back to Draft.</b> "Can you add the two bedside
/// tables" is the commonest thing that happens to a quotation, and the shop
/// has to be able to change it and send it again. What it cannot do is change
/// one the customer has already accepted — at that point the figures are an
/// agreement, and a new agreement is a new quotation.
/// </para>
/// <para>
/// <b>Declined is not terminal; Converted is.</b> A customer who says no and
/// then telephones a week later is ordinary, and re-sending is kinder than
/// making them ask twice. Once it is an order there is nothing left to do to
/// the quotation, and the order has its own life from there.
/// </para>
/// </remarks>
public static class QuotationStatusMachine
{
    private static readonly Dictionary<QuotationStatus, QuotationStatus[]> Allowed = new()
    {
        [QuotationStatus.Draft] = [QuotationStatus.Sent],

        [QuotationStatus.Sent] =
        [
            // Back to the workshop: "can you add the bedside tables".
            QuotationStatus.Draft,
            QuotationStatus.Accepted,
            QuotationStatus.Declined,
            QuotationStatus.Expired
        ],

        // Accepted but not yet placed. The shop converts it; nobody edits it.
        [QuotationStatus.Accepted] = [QuotationStatus.Converted, QuotationStatus.Declined],

        // Somebody who said no last week and has telephoned today.
        [QuotationStatus.Declined] = [QuotationStatus.Draft],

        // Re-quoted rather than honoured: timber prices move.
        [QuotationStatus.Expired] = [QuotationStatus.Draft],

        [QuotationStatus.Converted] = []
    };

    public static bool CanTransition(QuotationStatus from, QuotationStatus to) =>
        Allowed.TryGetValue(from, out var next) && Array.IndexOf(next, to) >= 0;

    public static IReadOnlyList<QuotationStatus> NextFrom(QuotationStatus status) =>
        Allowed.TryGetValue(status, out var next) ? next : [];

    public static bool IsTerminal(QuotationStatus status) =>
        Allowed.TryGetValue(status, out var next) && next.Length == 0;

    /// <summary>
    /// Whether the figures may still be edited.
    /// </summary>
    /// <remarks>
    /// Only a draft. A sent quotation whose lines could still be changed is a
    /// quotation the customer cannot rely on, and the whole value of one is
    /// that it can be relied on. Changing a sent one means pulling it back to
    /// Draft first, which is a deliberate act and leaves a trail.
    /// </remarks>
    public static bool IsEditable(QuotationStatus status) => status == QuotationStatus.Draft;

    /// <summary>
    /// Whether the customer may still accept or decline it themselves.
    /// </summary>
    public static bool IsAnswerable(QuotationStatus status) => status == QuotationStatus.Sent;

    /// <summary>
    /// Whether it has run out of time.
    /// </summary>
    /// <remarks>
    /// Asked of a date rather than stored as a flag, so a quotation does not
    /// depend on a job having run to be correct about itself. The job that
    /// writes <see cref="QuotationStatus.Expired"/> makes the fact durable for
    /// reporting; this makes it true immediately.
    /// </remarks>
    public static bool HasLapsed(QuotationStatus status, DateOnly validUntil, DateOnly todayInDhaka) =>
        status == QuotationStatus.Sent && validUntil < todayInDhaka;
}
