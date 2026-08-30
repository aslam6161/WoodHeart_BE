namespace WoodHeart.Domain.Entity;

/// <summary>
/// The base for every persisted entity: a <see cref="long"/> identity plus
/// audit columns.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why <see cref="long"/> and not <see cref="Guid"/>.</b> Identity columns
/// cluster naturally, keep indexes half the width of a GUID, and — the reason
/// that actually matters day to day — a customer can read an order id down a
/// phone line to support. Anything a customer or courier quotes aloud gets a
/// separate human-facing number on top of this (<c>WH-2608-00042</c>); the
/// <see cref="Id"/> stays an internal key.
/// </para>
/// <para>
/// <b>Why <see cref="CreatedAt"/> has no initialiser.</b> Bento's
/// <c>BaseEntity</c> defaults it to <c>DateTime.UtcNow</c>, which reads
/// harmlessly but makes every time-dependent rule untestable: you cannot write
/// a test for "this discount expired yesterday" against a field that always
/// says now. The stamping moved into <c>DataContext</c>, which holds the
/// injected clock, so tests can wind time forward and back.
/// </para>
/// <para>
/// <b>Why <see cref="DateTimeOffset"/> and not <see cref="DateTime"/>.</b>
/// Bangladesh is UTC+06 with no daylight saving, so the offset never shifts —
/// but a bare <c>DateTime</c> loses the information that a value is UTC at all,
/// and the six-hour class of bug it produces (an order timestamped 02:00
/// showing as the previous day in a Dhaka-local report) is invisible until
/// someone reconciles a day's sales.
/// </para>
/// </remarks>
public abstract class BaseEntity : IBaseEntity
{
    public long Id { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public long? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public long? UpdatedBy { get; set; }

    /// <summary>
    /// PostgreSQL's system column, mapped as a concurrency token.
    /// </summary>
    /// <remarks>
    /// Costs nothing — the column already exists on every row — and turns the
    /// two writes that matter into a detectable conflict rather than a silent
    /// overwrite: two admins editing one product, and two checkouts drawing
    /// down the last unit of stock.
    /// </remarks>
    public uint Version { get; set; }
}

/// <summary>
/// A <see cref="BaseEntity"/> that is hidden on delete rather than removed.
/// </summary>
public abstract class SoftDeletableEntity : BaseEntity, ISoftDeletable
{
    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public long? DeletedBy { get; set; }
}
