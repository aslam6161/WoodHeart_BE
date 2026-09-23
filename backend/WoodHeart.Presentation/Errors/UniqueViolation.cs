using Microsoft.EntityFrameworkCore;
using Npgsql;
using WoodHeart.Domain.Constants;

namespace WoodHeart.Presentation.Errors;

/// <summary>
/// Turns a database unique-index violation into something a client can act on.
/// </summary>
/// <remarks>
/// <para>
/// <b>Some races cannot be won in application code, and this is where losing
/// one is answered.</b> Two customers pick the same four-o'clock consultation
/// a millisecond apart; both availability checks say it is free, both write,
/// and the index refuses the second. The alternative to handling it here is a
/// 500 and a customer who thinks the site is broken rather than one who picks
/// another time.
/// </para>
/// <para>
/// Here rather than in the services because this is where Entity Framework and
/// Npgsql already live — the Service layer deliberately knows about neither,
/// and a service that had to catch <c>DbUpdateException</c> would have to
/// reference both to do it.
/// </para>
/// <para>
/// Keyed on the constraint name, so the message names the actual collision
/// rather than saying "that already exists" about a booking. An index not in
/// the table still becomes a 409 — a unique violation is a conflict whatever
/// it was over — with wording that does not pretend to know more than it does.
/// </para>
/// </remarks>
public static class UniqueViolation
{
    /// <summary>PostgreSQL's SQLSTATE for a unique or exclusion violation.</summary>
    private const string UniqueViolationState = "23505";

    /// <summary>What to tell the client, per index the client can actually hit.</summary>
    private static readonly Dictionary<string, (string? Code, string Message)> Known = new(StringComparer.Ordinal)
    {
        ["ux_bookings_consultant_slot"] = (
            ConsultationErrors.SlotTaken,
            "Somebody booked that time while you were filling in the form. Please choose another."),

        ["ux_bookings_idempotency_key"] = (
            ConsultationErrors.SlotTaken,
            "That booking has already been made."),

        ["ux_promotion_usages_discount_order"] = (
            "promotions.coupon_limit_reached.conflict",
            "That discount has already been recorded against this order.")
    };

    /// <summary>
    /// The conflict this exception describes, or null when it is not one.
    /// </summary>
    public static (string? Code, string Message)? Describe(Exception exception)
    {
        if (exception is not DbUpdateException { InnerException: PostgresException postgres }
            || !string.Equals(postgres.SqlState, UniqueViolationState, StringComparison.Ordinal))
        {
            return null;
        }

        return Known.TryGetValue(postgres.ConstraintName ?? string.Empty, out var known)
            ? known
            : (null, "That already exists. Reload and try again.");
    }
}
