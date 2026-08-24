using WoodHeart.Application.Common.Abstractions;

namespace WoodHeart.Infrastructure.Services;

/// <summary>
/// The real clock, plus Dhaka-local conversions.
/// </summary>
/// <remarks>
/// <para>
/// Everything is stored in UTC and displayed in Asia/Dhaka. Bangladesh is
/// UTC+6 with no daylight saving, which makes the conversion trivial — but it
/// still goes through <see cref="TimeZoneInfo"/> rather than a hardcoded
/// <c>AddHours(6)</c>, because a hardcoded offset is exactly the kind of thing
/// that silently breaks if the business ever ships to a second country.
/// </para>
/// <para>
/// The timezone id differs by platform — Windows says "Bangladesh Standard
/// Time", Linux says "Asia/Dhaka" — and the API runs on Linux in production but
/// Windows on this dev machine, so both are tried.
/// </para>
/// </remarks>
public sealed class DateTimeProvider : IDateTimeProvider
{
    private static readonly TimeZoneInfo DhakaTimeZone = ResolveDhakaTimeZone();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateTimeOffset DhakaNow => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, DhakaTimeZone);

    public DateOnly DhakaToday => DateOnly.FromDateTime(DhakaNow.DateTime);

    public DateTimeOffset DhakaToUtc(DateOnly date, TimeOnly time)
    {
        var unspecified = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
        var offset = DhakaTimeZone.GetUtcOffset(unspecified);

        return new DateTimeOffset(unspecified, offset).ToUniversalTime();
    }

    public DateTimeOffset ToDhaka(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, DhakaTimeZone);

    private static TimeZoneInfo ResolveDhakaTimeZone()
    {
        foreach (var id in new[] { "Asia/Dhaka", "Bangladesh Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // Try the next platform-specific id.
            }
            catch (InvalidTimeZoneException)
            {
                // Corrupt tz database entry — fall through to the fixed offset.
            }
        }

        // Last resort so the app still starts on a stripped-down container image.
        return TimeZoneInfo.CreateCustomTimeZone("BST", TimeSpan.FromHours(6), "Bangladesh Standard Time", "BST");
    }
}
