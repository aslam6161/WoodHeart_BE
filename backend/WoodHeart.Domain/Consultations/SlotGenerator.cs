namespace WoodHeart.Domain.Consultations;

/// <summary>One window in a consultant's ordinary week, as the generator sees it.</summary>
/// <param name="SlotMinutes">How far apart the offered starting times are.</param>
public readonly record struct AvailabilityWindow(
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int SlotMinutes);

/// <summary>A date that is not the ordinary week.</summary>
/// <param name="IsClosed">Nothing at all that day; the times are then ignored.</param>
public readonly record struct ScheduleException(
    DateOnly Date,
    bool IsClosed,
    TimeOnly? StartTime = null,
    TimeOnly? EndTime = null);

/// <summary>An appointment already in the diary.</summary>
public readonly record struct BookedSlot(DateTimeOffset StartUtc, int DurationMinutes)
{
    public DateTimeOffset EndUtc => StartUtc.AddMinutes(DurationMinutes);
}

/// <summary>What is being asked for.</summary>
/// <param name="From">The first Dhaka date to look at, inclusive.</param>
/// <param name="To">The last Dhaka date to look at, inclusive.</param>
/// <param name="DurationMinutes">How long the appointment is.</param>
/// <param name="BufferBeforeMinutes">Time that must be clear before it.</param>
/// <param name="BufferAfterMinutes">Time that must be clear after it.</param>
public readonly record struct SlotRequest(
    DateOnly From,
    DateOnly To,
    int DurationMinutes,
    int BufferBeforeMinutes = 0,
    int BufferAfterMinutes = 0);

/// <summary>
/// Turns a consultant's week into the times a customer can actually pick.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pure: no database, no clock, no time-zone database.</b> "Now" arrives as
/// an argument for the same reason the discount engine's does — this is the
/// one function that decides what is free, it runs on the booking page and
/// again when the booking is placed, and two implementations of "is four
/// o'clock available" is how two customers end up in the studio at once.
/// </para>
/// <para>
/// <b>The arithmetic is done in Dhaka and the answers are UTC.</b> A rule says
/// "Sunday, ten in the morning", which is not a moment until it meets a date;
/// combining the two in Dhaka and converting the result is the only order that
/// survives a server running somewhere else. Bangladesh keeps +06:00 all year
/// with no daylight saving, so a fixed offset is exact rather than an
/// approximation — the reason a time-zone lookup is not needed here at all.
/// </para>
/// <para>
/// <b>Buffers apply to the candidate, not to the booking already in the
/// diary.</b> Expanding the slot being considered by the buffer at each end
/// leaves exactly that much clear between it and its neighbours, once rather
/// than twice. Expanding both would silently double every gap, which reads on
/// a calendar as a consultant who is mysteriously unavailable for half the
/// afternoon.
/// </para>
/// <para>
/// <b>The appointment must fit inside the working window; the buffers need
/// not.</b> A consultant's journey home after the last visit of the day is not
/// the shop's business, and requiring the buffer to fit would quietly delete
/// the last slot of every day.
/// </para>
/// </remarks>
public static class SlotGenerator
{
    /// <summary>Bangladesh keeps one offset all year, so this is exact.</summary>
    public static readonly TimeSpan DhakaOffset = TimeSpan.FromHours(6);

    /// <summary>
    /// The most days one request may span.
    /// </summary>
    /// <remarks>
    /// A calendar shows a month; a request for two years would be a client bug
    /// or somebody probing, and either way it is a lot of arithmetic to do on
    /// their behalf.
    /// </remarks>
    public const int MaxDays = 62;

    /// <summary>
    /// Every startable time between two dates, in UTC and in order.
    /// </summary>
    /// <param name="now">
    /// The instant a slot must be after. A time already past is not on offer,
    /// however free the consultant is.
    /// </param>
    public static IReadOnlyList<DateTimeOffset> Generate(
        SlotRequest request,
        IReadOnlyList<AvailabilityWindow> windows,
        IReadOnlyList<ScheduleException> exceptions,
        IReadOnlyList<BookedSlot> taken,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(exceptions);
        ArgumentNullException.ThrowIfNull(taken);

        if (request.DurationMinutes <= 0 || request.To < request.From)
        {
            return [];
        }

        var slots = new List<DateTimeOffset>();
        var lastDate = Cap(request.From, request.To);

        for (var date = request.From; date <= lastDate; date = date.AddDays(1))
        {
            foreach (var window in WindowsFor(date, windows, exceptions))
            {
                AddSlots(slots, date, window, request, taken, now);
            }
        }

        // Sorted and de-duplicated: two overlapping rules on one day are a
        // mistake in the schedule, not a reason to offer the same o'clock
        // twice on the page.
        return [.. slots.Distinct().Order()];
    }

    /// <summary>
    /// The windows that apply on one date: the weekly rules, or what the
    /// exception says instead.
    /// </summary>
    /// <remarks>
    /// An exception replaces the day rather than trimming it. A half day is
    /// "open 10 to 1", not "closed 1 to 6", because the first is what somebody
    /// writing it down would say — and because the second cannot express a day
    /// that opens late.
    /// </remarks>
    private static List<AvailabilityWindow> WindowsFor(
        DateOnly date,
        IReadOnlyList<AvailabilityWindow> windows,
        IReadOnlyList<ScheduleException> exceptions)
    {
        var ordinary = windows.Where(window => window.DayOfWeek == date.DayOfWeek).ToList();

        ScheduleException? found = null;

        foreach (var entry in exceptions)
        {
            if (entry.Date == date)
            {
                found = entry;
                break;
            }
        }

        if (found is not { } exception)
        {
            return ordinary;
        }

        if (exception.IsClosed)
        {
            return [];
        }

        if (exception.StartTime is not { } start || exception.EndTime is not { } end)
        {
            // An exception that is neither closed nor a window says nothing.
            // Treated as an ordinary day rather than as a closure, because
            // silently deleting a day over a half-filled form is the more
            // expensive mistake.
            return ordinary;
        }

        // The step comes from the day's own rules where there are any, so a
        // half day keeps the cadence customers are used to. Where the
        // consultant does not normally work that day at all, the appointment's
        // own length is the only sensible step.
        var step = ordinary.Count > 0 ? ordinary[0].SlotMinutes : 0;

        return [new AvailabilityWindow(date.DayOfWeek, start, end, step)];
    }

    private static void AddSlots(
        List<DateTimeOffset> slots,
        DateOnly date,
        AvailabilityWindow window,
        SlotRequest request,
        IReadOnlyList<BookedSlot> taken,
        DateTimeOffset now)
    {
        var step = window.SlotMinutes > 0 ? window.SlotMinutes : request.DurationMinutes;

        if (step <= 0 || window.EndTime <= window.StartTime)
        {
            return;
        }

        // Counted in minutes from midnight rather than walked with
        // TimeOnly.AddMinutes, which wraps: a step that runs past the end of
        // the day would come back round as an earlier time and the loop would
        // never end. The bound also states the rule plainly — the appointment
        // has to finish inside the window.
        var opens = (window.StartTime.Hour * 60) + window.StartTime.Minute;
        var closes = (window.EndTime.Hour * 60) + window.EndTime.Minute;

        for (var minute = opens; minute + request.DurationMinutes <= closes; minute += step)
        {
            var startUtc = ToUtc(date, new TimeOnly(minute / 60, minute % 60));

            if (startUtc > now && !Conflicts(startUtc, request, taken))
            {
                slots.Add(startUtc);
            }
        }
    }

    /// <summary>Whether this candidate, with its buffers, runs into anything booked.</summary>
    private static bool Conflicts(
        DateTimeOffset startUtc, SlotRequest request, IReadOnlyList<BookedSlot> taken)
    {
        var from = startUtc.AddMinutes(-request.BufferBeforeMinutes);
        var to = startUtc.AddMinutes(request.DurationMinutes + request.BufferAfterMinutes);

        // Half-open intervals: an appointment ending at 3 and one starting at 3
        // do not overlap, which is what anybody looking at a diary would say.
        return taken.Any(booked => from < booked.EndUtc && booked.StartUtc < to);
    }

    /// <summary>A Dhaka date and time as the instant it actually is.</summary>
    public static DateTimeOffset ToUtc(DateOnly date, TimeOnly time) =>
        new DateTimeOffset(date.ToDateTime(time), DhakaOffset).ToUniversalTime();

    private static DateOnly Cap(DateOnly from, DateOnly to)
    {
        var limit = from.AddDays(MaxDays - 1);

        return to > limit ? limit : to;
    }
}
