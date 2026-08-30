namespace WoodHeart.Domain.Helpers;

/// <summary>
/// The clock. Inject this; never call <c>DateTime.UtcNow</c> directly.
/// </summary>
/// <remarks>
/// <para>
/// This interface lives in Domain rather than Service because
/// <c>DataContext</c> needs it to stamp audit fields, and Repository cannot
/// reference Service. The implementation is in
/// <c>Service/Infrastructure/Time</c>.
/// </para>
/// <para>
/// Nearly everything WoodHeart sells or schedules is time-dependent: discount
/// windows, consultation slots, stock reservation expiry, delivery estimates,
/// "orders placed today". Every one of those is untestable against a static
/// <c>DateTime.UtcNow</c>, and untestable time-dependent logic is where
/// off-by-one-day bugs live.
/// </para>
/// <para>
/// Storage is UTC, display is Dhaka. The Dhaka members exist because the
/// business day is a Dhaka-local concept — a sale placed at 02:00 UTC belongs
/// to the Bangladeshi day that has already started, and a report that gets that
/// wrong is off by a whole day's revenue.
/// </para>
/// </remarks>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }

    DateTimeOffset DhakaNow { get; }

    /// <summary>Today in Dhaka. What "today's orders" means to the shop.</summary>
    DateOnly DhakaToday { get; }

    /// <summary>
    /// Converts a Dhaka-local date and time to UTC — how a consultation slot
    /// chosen in the admin UI becomes a stored instant.
    /// </summary>
    DateTimeOffset DhakaToUtc(DateOnly date, TimeOnly time);

    DateTimeOffset ToDhaka(DateTimeOffset utc);
}
