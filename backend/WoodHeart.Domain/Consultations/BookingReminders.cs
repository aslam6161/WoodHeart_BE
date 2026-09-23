using WoodHeart.Domain.Enums.Consultations;

namespace WoodHeart.Domain.Consultations;

/// <summary>
/// Which reminder a booking is owed right now, if any.
/// </summary>
/// <remarks>
/// <para>
/// A pure function for the same reason the slot generator is one: "did we
/// already tell them, and is it too early to tell them again" is arithmetic,
/// and arithmetic that decides whether the shop pays an SMS gateway twice
/// should be readable in one place and testable without a database.
/// </para>
/// <para>
/// <b>The two reminders are bands, not moments.</b> The day-before reminder is
/// owed whenever the appointment is between two and twenty-four hours away;
/// the last one whenever it is under two. Written as moments — "fire at
/// exactly T−24h" — a job that was down for twenty minutes would skip the
/// reminder entirely, and the customer misses the appointment because the
/// server had a bad morning. As bands, a late run still sends, and sends once.
/// </para>
/// <para>
/// <b>The bands cannot overlap, so nobody gets two messages in a minute.</b>
/// Somebody who books at nine in the morning for four in the afternoon is
/// inside the day-before band immediately: they get that message now and the
/// last one at two. Somebody who books an hour ahead is already past it, and
/// gets only the last one. That is why the wording of the message says the
/// time rather than "tomorrow" — see the template.
/// </para>
/// <para>
/// <b>Only a booking the shop has agreed to is reminded.</b> A Requested
/// booking is one nobody has looked at yet, and "see you at four" for an
/// appointment the studio is not expecting is worse than silence. The board is
/// where an unconfirmed booking approaching its date should be noticed.
/// </para>
/// </remarks>
public static class BookingReminders
{
    /// <summary>The day-before reminder, in hours before the appointment.</summary>
    public const int FirstHoursBefore = 24;

    /// <summary>The last one, near enough to leave the house for.</summary>
    public const int FinalHoursBefore = 2;

    /// <summary>
    /// Which reminder is owed, as hours before the appointment, or null for
    /// none.
    /// </summary>
    /// <param name="scheduledAtUtc">When the appointment starts.</param>
    /// <param name="now">The clock.</param>
    /// <param name="firstSentAt">When the day-before one went, if it did.</param>
    /// <param name="finalSentAt">When the last one went, if it did.</param>
    public static int? Due(
        DateTimeOffset scheduledAtUtc,
        DateTimeOffset now,
        DateTimeOffset? firstSentAt,
        DateTimeOffset? finalSentAt)
    {
        var minutesAway = (scheduledAtUtc - now).TotalMinutes;

        // It has started, or it is over. Nothing useful left to say, and a
        // reminder for a consultation somebody is sitting in is noise.
        if (minutesAway <= 0)
        {
            return null;
        }

        if (minutesAway <= FinalHoursBefore * 60)
        {
            return finalSentAt is null ? FinalHoursBefore : null;
        }

        if (minutesAway <= FirstHoursBefore * 60)
        {
            return firstSentAt is null ? FirstHoursBefore : null;
        }

        // Too far off to be worth a message yet.
        return null;
    }

    /// <summary>
    /// Whether a booking in this status should be reminded about at all.
    /// </summary>
    /// <remarks>
    /// The shop has agreed to it, and it has not happened yet. Requested is
    /// deliberately absent: telling a customer to turn up somewhere nobody has
    /// agreed to see them is how two people arrive at a studio expecting
    /// different things.
    /// </remarks>
    public static bool WorthReminding(BookingStatus status) =>
        Array.IndexOf(Remindable, status) >= 0;

    /// <summary>
    /// The same set as an array, for the query that finds them.
    /// </summary>
    /// <remarks>
    /// EF cannot translate a method call into SQL, so the repository needs the
    /// values themselves. It reads them from here rather than spelling them
    /// out again — one list, so the job and the query cannot come to disagree
    /// about which bookings are worth a message.
    /// </remarks>
    public static readonly BookingStatus[] Remindable =
    [
        BookingStatus.Confirmed,
        BookingStatus.Rescheduled
    ];
}
