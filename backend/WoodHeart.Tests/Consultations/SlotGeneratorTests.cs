using WoodHeart.Domain.Consultations;

namespace WoodHeart.Tests.Consultations;

/// <summary>
/// The slot generator.
/// </summary>
/// <remarks>
/// <para>
/// PLAN.md §12 names slot generation among the domain functions that get a
/// unit suite with no mocks, and the reason is the same as the discount
/// engine's: this decides what a customer is offered, it runs again when they
/// press Book, and the two must not be able to disagree.
/// </para>
/// <para>
/// The dates are real ones. 2026-09-27 is a Sunday and 2026-09-25 is a Friday —
/// the weekend here — so a suite that had quietly assumed a Saturday–Sunday
/// week would fail rather than pass by luck.
/// </para>
/// </remarks>
public class SlotGeneratorTests
{
    private static readonly DateOnly Sunday = new(2026, 9, 27);
    private static readonly DateOnly Monday = new(2026, 9, 28);
    private static readonly DateOnly Friday = new(2026, 9, 25);

    /// <summary>Well before every date below, so nothing is filtered as past.</summary>
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 0, 0, 0, TimeSpan.Zero);

    // -------------------------------------------------------------------------
    // The ordinary week
    // -------------------------------------------------------------------------

    [Fact]
    public void A_window_offers_a_start_every_step()
    {
        // 10:00 to 13:00, on the half hour, for an hour each: the last start
        // is noon, because half past would finish at half past one.
        var slots = Generate(Sunday, Sunday, duration: 60, Rule(DayOfWeek.Sunday, 10, 13, 30));

        slots.ShouldBe(
        [
            At(Sunday, 10, 0), At(Sunday, 10, 30), At(Sunday, 11, 0),
            At(Sunday, 11, 30), At(Sunday, 12, 0)
        ]);
    }

    [Fact]
    public void The_appointment_has_to_finish_inside_the_window()
    {
        // A ninety-minute site visit in the same three hours. Four starts, not
        // five: a consultation that runs past the end of the day is not a slot.
        var slots = Generate(Sunday, Sunday, duration: 90, Rule(DayOfWeek.Sunday, 10, 13, 30));

        slots.Count.ShouldBe(4);
        slots[^1].ShouldBe(At(Sunday, 11, 30));
    }

    [Fact]
    public void The_times_are_Dhaka_and_the_answers_are_UTC()
    {
        // Ten in the morning in Dhaka is four in the morning in UTC. Getting
        // this the wrong way round offers a customer six o'clock and books
        // them for midnight.
        var slots = Generate(Sunday, Sunday, duration: 60, Rule(DayOfWeek.Sunday, 10, 11, 60));

        var only = slots.ShouldHaveSingleItem();

        only.Offset.ShouldBe(TimeSpan.Zero);
        only.Hour.ShouldBe(4);
        only.ToOffset(SlotGenerator.DhakaOffset).Hour.ShouldBe(10);
    }

    [Fact]
    public void A_day_the_consultant_does_not_work_offers_nothing()
    {
        // Friday is the weekend here. Nothing in the generator assumes which
        // days those are — the shop says so by which rules it writes.
        Generate(Friday, Friday, duration: 60, Rule(DayOfWeek.Sunday, 10, 13, 30)).ShouldBeEmpty();
    }

    [Fact]
    public void Two_windows_on_one_day_are_a_lunch_break()
    {
        var slots = Generate(
            Sunday,
            Sunday,
            duration: 60,
            Rule(DayOfWeek.Sunday, 10, 12, 60),
            Rule(DayOfWeek.Sunday, 14, 16, 60));

        slots.ShouldBe([At(Sunday, 10), At(Sunday, 11), At(Sunday, 14), At(Sunday, 15)]);
    }

    [Fact]
    public void Two_rules_that_overlap_do_not_offer_the_same_time_twice()
    {
        // A mistake in the schedule, not a reason to draw ten o'clock twice on
        // the page.
        var slots = Generate(
            Sunday,
            Sunday,
            duration: 60,
            Rule(DayOfWeek.Sunday, 10, 12, 60),
            Rule(DayOfWeek.Sunday, 10, 13, 60));

        slots.ShouldBe([At(Sunday, 10), At(Sunday, 11), At(Sunday, 12)]);
    }

    [Fact]
    public void A_range_covers_every_date_in_it()
    {
        var slots = Generate(
            Sunday,
            Monday,
            duration: 60,
            Rule(DayOfWeek.Sunday, 10, 11, 60),
            Rule(DayOfWeek.Monday, 15, 16, 60));

        slots.ShouldBe([At(Sunday, 10), At(Monday, 15)]);
    }

    // -------------------------------------------------------------------------
    // Days that are not ordinary
    // -------------------------------------------------------------------------

    [Fact]
    public void A_closed_day_removes_the_whole_day()
    {
        var slots = Generate(
            Sunday,
            Sunday,
            duration: 60,
            rules: [Rule(DayOfWeek.Sunday, 10, 13, 30)],
            exceptions: [new ScheduleException(Sunday, IsClosed: true)]);

        slots.ShouldBeEmpty();
    }

    [Fact]
    public void A_half_day_replaces_the_window_and_keeps_the_cadence()
    {
        // "Open 10 to 12" rather than "closed 12 to 6", because the first is
        // what somebody writing it down would say — and the half hours are
        // still half hours, so the day looks like the days around it.
        var slots = Generate(
            Sunday,
            Sunday,
            duration: 60,
            rules: [Rule(DayOfWeek.Sunday, 10, 17, 30)],
            exceptions:
            [
                new ScheduleException(Sunday, IsClosed: false, new TimeOnly(10, 0), new TimeOnly(12, 0))
            ]);

        slots.ShouldBe([At(Sunday, 10), At(Sunday, 10, 30), At(Sunday, 11)]);
    }

    [Fact]
    public void An_exception_that_says_nothing_leaves_the_day_alone()
    {
        // Neither closed nor a window: a half-filled form. Treated as an
        // ordinary day, because silently deleting one is the more expensive
        // way to be wrong.
        var slots = Generate(
            Sunday,
            Sunday,
            duration: 60,
            rules: [Rule(DayOfWeek.Sunday, 10, 12, 60)],
            exceptions: [new ScheduleException(Sunday, IsClosed: false)]);

        slots.ShouldBe([At(Sunday, 10), At(Sunday, 11)]);
    }

    [Fact]
    public void A_window_on_a_day_with_no_rules_steps_by_the_appointment()
    {
        // The consultant does not normally work Friday, but is this Friday.
        // There is no cadence to keep, so the appointment's own length is the
        // only sensible step.
        var slots = Generate(
            Friday,
            Friday,
            duration: 60,
            rules: [Rule(DayOfWeek.Sunday, 10, 13, 30)],
            exceptions:
            [
                new ScheduleException(Friday, IsClosed: false, new TimeOnly(9, 0), new TimeOnly(12, 0))
            ]);

        slots.ShouldBe([At(Friday, 9), At(Friday, 10), At(Friday, 11)]);
    }

    // -------------------------------------------------------------------------
    // The diary
    // -------------------------------------------------------------------------

    [Fact]
    public void A_booking_takes_the_times_it_overlaps()
    {
        var slots = Generate(
            Sunday,
            Sunday,
            duration: 60,
            rules: [Rule(DayOfWeek.Sunday, 10, 14, 60)],
            taken: [new BookedSlot(At(Sunday, 11), 60)]);

        slots.ShouldBe([At(Sunday, 10), At(Sunday, 12), At(Sunday, 13)]);
    }

    [Fact]
    public void A_slot_that_starts_where_a_booking_ends_is_free()
    {
        // Half-open intervals: an appointment ending at eleven and one
        // starting at eleven do not overlap, which is what anybody looking at
        // a diary would say. Buffers are what put a gap there, and there are
        // none here.
        var slots = Generate(
            Sunday,
            Sunday,
            duration: 60,
            rules: [Rule(DayOfWeek.Sunday, 10, 12, 60)],
            taken: [new BookedSlot(At(Sunday, 10), 60)]);

        slots.ShouldBe([At(Sunday, 11)]);
    }

    [Fact]
    public void A_buffer_clears_time_on_each_side_of_a_booking()
    {
        // An hour booked at noon, with thirty minutes wanted either side: the
        // eleven o'clock would finish at noon and the one o'clock would start
        // as the consultant walks back in, so both go.
        var slots = SlotGenerator.Generate(
            new SlotRequest(Sunday, Sunday, DurationMinutes: 60, BufferBeforeMinutes: 30, BufferAfterMinutes: 30),
            [Rule(DayOfWeek.Sunday, 10, 16, 60)],
            [],
            [new BookedSlot(At(Sunday, 12), 60)],
            Now);

        slots.ShouldBe([At(Sunday, 10), At(Sunday, 14), At(Sunday, 15)]);
    }

    [Fact]
    public void The_buffer_does_not_have_to_fit_inside_the_working_day()
    {
        // The consultant's journey home after the last visit is not the shop's
        // business. Requiring the buffer to fit would quietly delete the last
        // slot of every day.
        var slots = SlotGenerator.Generate(
            new SlotRequest(Sunday, Sunday, DurationMinutes: 60, BufferAfterMinutes: 60),
            [Rule(DayOfWeek.Sunday, 10, 12, 60)],
            [],
            [],
            Now);

        slots.ShouldBe([At(Sunday, 10), At(Sunday, 11)]);
    }

    // -------------------------------------------------------------------------
    // Guards
    // -------------------------------------------------------------------------

    [Fact]
    public void Nothing_already_past_is_offered()
    {
        // However free the consultant is, eleven o'clock this morning is not
        // bookable at noon.
        var slots = SlotGenerator.Generate(
            new SlotRequest(Sunday, Sunday, DurationMinutes: 60),
            [Rule(DayOfWeek.Sunday, 10, 14, 60)],
            [],
            [],
            At(Sunday, 11, 30));

        slots.ShouldBe([At(Sunday, 12), At(Sunday, 13)]);
    }

    [Fact]
    public void A_step_longer_than_the_window_offers_the_first_time_and_stops()
    {
        // The regression this guards: walking the clock with an add that wraps
        // past midnight comes back round as an earlier time, and the loop
        // never ends.
        var slots = Generate(Sunday, Sunday, duration: 60, Rule(DayOfWeek.Sunday, 10, 12, 600));

        slots.ShouldBe([At(Sunday, 10)]);
    }

    [Fact]
    public void An_appointment_with_no_length_asks_for_nothing()
    {
        Generate(Sunday, Sunday, duration: 0, Rule(DayOfWeek.Sunday, 10, 13, 30)).ShouldBeEmpty();
    }

    [Fact]
    public void A_range_that_ends_before_it_starts_asks_for_nothing()
    {
        Generate(Monday, Sunday, duration: 60, Rule(DayOfWeek.Sunday, 10, 13, 30)).ShouldBeEmpty();
    }

    [Fact]
    public void A_range_longer_than_the_cap_is_trimmed_rather_than_computed()
    {
        // A calendar shows a month. Two years would be a client bug or
        // somebody probing, and either way it is a lot of arithmetic to do on
        // their behalf.
        var slots = Generate(
            Sunday, Sunday.AddYears(1), duration: 60, Rule(DayOfWeek.Sunday, 10, 11, 60));

        // One slot a week, over the capped window rather than the year asked for.
        slots.Count.ShouldBe(SlotGenerator.MaxDays / 7 + 1);
        slots[0].ShouldBe(At(Sunday, 10));
        slots[^1].ShouldBeLessThan(At(Sunday.AddDays(SlotGenerator.MaxDays), 0));
    }

    // -------------------------------------------------------------------------
    // Fixtures
    // -------------------------------------------------------------------------

    private static IReadOnlyList<DateTimeOffset> Generate(
        DateOnly from, DateOnly to, int duration, params AvailabilityWindow[] rules) =>
        SlotGenerator.Generate(new SlotRequest(from, to, duration), rules, [], [], Now);

    private static IReadOnlyList<DateTimeOffset> Generate(
        DateOnly from,
        DateOnly to,
        int duration,
        IReadOnlyList<AvailabilityWindow> rules,
        IReadOnlyList<ScheduleException>? exceptions = null,
        IReadOnlyList<BookedSlot>? taken = null) =>
        SlotGenerator.Generate(
            new SlotRequest(from, to, duration), rules, exceptions ?? [], taken ?? [], Now);

    private static AvailabilityWindow Rule(DayOfWeek day, int fromHour, int toHour, int step) =>
        new(day, new TimeOnly(fromHour, 0), new TimeOnly(toHour, 0), step);

    private static DateTimeOffset At(DateOnly date, int hour, int minute = 0) =>
        SlotGenerator.ToUtc(date, new TimeOnly(hour, minute));
}
