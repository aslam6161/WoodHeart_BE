using WoodHeart.Domain.Helpers;

namespace WoodHeart.Tests.Helper;

/// <summary>
/// A clock the test controls.
/// </summary>
/// <remarks>
/// <para>
/// This is why <see cref="IDateTimeProvider"/> exists as a port rather than a
/// call to <c>DateTime.UtcNow</c>. Almost everything valuable in WoodHeart is
/// time-dependent — consultation slots, discount windows, stock-reservation
/// expiry, booking reminders — and none of it can be tested honestly against a
/// clock that cannot be moved.
/// </para>
/// <para>
/// Defaults to a fixed Friday, deliberately: Friday is a weekend day in
/// Bangladesh, so any consultation-availability logic that assumes a
/// Saturday–Sunday weekend fails loudly rather than passing by luck.
/// </para>
/// </remarks>
public sealed class FakeClock(DateTimeOffset? utcNow = null) : IDateTimeProvider
{
    private static readonly TimeSpan DhakaOffset = TimeSpan.FromHours(6);

    /// <summary>Friday 2026-08-28, 09:00 UTC = 15:00 in Dhaka.</summary>
    public static readonly DateTimeOffset DefaultNow =
        new(2026, 8, 28, 9, 0, 0, TimeSpan.Zero);

    public DateTimeOffset UtcNow { get; private set; } = utcNow ?? DefaultNow;

    public DateTimeOffset DhakaNow => UtcNow.ToOffset(DhakaOffset);

    public DateOnly DhakaToday => DateOnly.FromDateTime(DhakaNow.DateTime);

    public DateTimeOffset DhakaToUtc(DateOnly date, TimeOnly time) =>
        new DateTimeOffset(date.ToDateTime(time), DhakaOffset).ToUniversalTime();

    public DateTimeOffset ToDhaka(DateTimeOffset utc) => utc.ToOffset(DhakaOffset);

    /// <summary>Moves the clock forward, for testing expiry and reminder windows.</summary>
    public FakeClock Advance(TimeSpan by)
    {
        UtcNow = UtcNow.Add(by);
        return this;
    }

    public FakeClock SetTo(DateTimeOffset utc)
    {
        UtcNow = utc;
        return this;
    }
}
