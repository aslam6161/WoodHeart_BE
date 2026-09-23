using WoodHeart.Domain.Consultations;
using WoodHeart.Domain.Enums.Consultations;

namespace WoodHeart.Tests.Consultations;

/// <summary>
/// Which reminder a booking is owed, and — the part that costs money — which
/// it is not.
/// </summary>
/// <remarks>
/// The arithmetic decides whether a customer gets one message, two, or two
/// within a minute of each other. Every case below is a shape the shop will
/// actually meet: an ordinary booking a week out, one made the same morning,
/// one made an hour before, and a job that ran late.
/// </remarks>
public class BookingRemindersTests
{
    private static readonly DateTimeOffset Appointment =
        new(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);

    private static int? Due(TimeSpan away, DateTimeOffset? first = null, DateTimeOffset? final = null) =>
        BookingReminders.Due(Appointment, Appointment - away, first, final);

    private static DateTimeOffset Sent => Appointment.AddDays(-2);

    // -------------------------------------------------------------------------
    // The two bands
    // -------------------------------------------------------------------------

    [Fact]
    public void Nothing_is_owed_a_week_out()
    {
        // The shop does not want to pay for a message nobody will remember.
        Due(TimeSpan.FromDays(7)).ShouldBeNull();
    }

    [Theory]
    [InlineData(24)]
    [InlineData(20)]
    [InlineData(3)]
    public void The_day_before_reminder_is_owed_anywhere_inside_its_band(int hoursAway)
    {
        Due(TimeSpan.FromHours(hoursAway)).ShouldBe(BookingReminders.FirstHoursBefore);
    }

    [Fact]
    public void A_minute_past_twenty_four_hours_is_still_too_early()
    {
        Due(TimeSpan.FromHours(24).Add(TimeSpan.FromMinutes(1))).ShouldBeNull();
    }

    [Theory]
    [InlineData(120)]
    [InlineData(45)]
    [InlineData(5)]
    public void The_last_reminder_is_owed_inside_two_hours(int minutesAway)
    {
        Due(TimeSpan.FromMinutes(minutesAway)).ShouldBe(BookingReminders.FinalHoursBefore);
    }

    // -------------------------------------------------------------------------
    // Not twice
    // -------------------------------------------------------------------------

    [Fact]
    public void A_reminder_already_sent_is_not_sent_again()
    {
        Due(TimeSpan.FromHours(20), first: Sent).ShouldBeNull();
        Due(TimeSpan.FromMinutes(30), final: Sent).ShouldBeNull();
    }

    [Fact]
    public void Inside_two_hours_the_day_before_reminder_is_no_longer_owed()
    {
        // The bands do not overlap, and this is why. Somebody who booked at
        // nine for four the same afternoon has already had the first message;
        // without this they would get the second one seconds later, and with a
        // different rule they would get a "tomorrow" message about today.
        Due(TimeSpan.FromMinutes(90), first: null, final: Sent).ShouldBeNull();
    }

    [Fact]
    public void A_booking_made_an_hour_before_gets_only_the_last_one()
    {
        var owed = Due(TimeSpan.FromHours(1));

        owed.ShouldBe(BookingReminders.FinalHoursBefore);

        // And then nothing, rather than a day-before reminder it is far too
        // late for.
        Due(TimeSpan.FromMinutes(20), final: Sent).ShouldBeNull();
    }

    [Fact]
    public void A_job_that_ran_late_still_sends()
    {
        // Written as moments — "fire at exactly T-24h" — twenty minutes of
        // downtime would lose the reminder entirely. As a band, the next run
        // picks it up. Late is worth having; missing is not.
        Due(TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(-40)))
            .ShouldBe(BookingReminders.FirstHoursBefore);
    }

    // -------------------------------------------------------------------------
    // Too late to be useful
    // -------------------------------------------------------------------------

    [Fact]
    public void Nothing_is_owed_once_it_has_started()
    {
        Due(TimeSpan.Zero).ShouldBeNull();
        Due(TimeSpan.FromMinutes(-30)).ShouldBeNull();
    }

    [Fact]
    public void A_consultation_last_month_is_not_reminded_about()
    {
        Due(TimeSpan.FromDays(-30)).ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // Which bookings at all
    // -------------------------------------------------------------------------

    [Fact]
    public void Only_a_booking_the_shop_has_agreed_to_is_reminded_about()
    {
        // "See you at four" for an appointment nobody at the shop has looked
        // at is worse than silence: the customer travels and the studio is not
        // expecting them.
        BookingReminders.WorthReminding(BookingStatus.Requested).ShouldBe(false);

        BookingReminders.WorthReminding(BookingStatus.Confirmed).ShouldBe(true);
        BookingReminders.WorthReminding(BookingStatus.Rescheduled).ShouldBe(true);
    }

    [Theory]
    [InlineData(BookingStatus.Completed)]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData(BookingStatus.NoShow)]
    public void A_booking_that_is_over_is_not_reminded_about(BookingStatus status)
    {
        BookingReminders.WorthReminding(status).ShouldBe(false);
    }

    [Fact]
    public void The_array_the_query_uses_and_the_predicate_the_job_uses_are_one_list()
    {
        // The repository cannot call a method inside a SQL query, so it reads
        // the array. If the two could drift, the query would fetch bookings the
        // job then skips — or, worse, skip ones it should have sent.
        var byPredicate = Enum.GetValues<BookingStatus>()
            .Where(BookingReminders.WorthReminding)
            .ToArray();

        byPredicate.ShouldBe(BookingReminders.Remindable, ignoreOrder: true);
    }
}
