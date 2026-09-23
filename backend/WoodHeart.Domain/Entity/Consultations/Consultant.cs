using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Domain.Entity.Consultations;

/// <summary>
/// A person whose time is being sold.
/// </summary>
/// <remarks>
/// <para>
/// Not an <c>AppUser</c>. A consultant is somebody the customer chooses on a
/// booking page — with a photograph, a biography and a list of what they are
/// good at — and most of them will never sign in to the admin panel. Tying the
/// two together would mean creating a login for a freelance architect who
/// visits on Thursdays, and it would make "hide this consultant" the same
/// operation as "revoke this account".
/// </para>
/// <para>
/// When a consultant does also work here, the two rows sit side by side and
/// nothing is lost.
/// </para>
/// </remarks>
public class Consultant : SoftDeletableEntity
{
    /// <summary>A person's name, as they write it. Not localized.</summary>
    /// <remarks>
    /// Deliberately a plain string where a product's name is a
    /// <see cref="LocalizedText"/>. A name is not translated; it is
    /// transliterated at best, and a form that asks for an English and a Bangla
    /// version of somebody's name invites two spellings of one person.
    /// </remarks>
    public string Name { get; set; } = null!;

    /// <summary>Relative storage key for the portrait, not a full URL.</summary>
    public string? PhotoPath { get; set; }

    public LocalizedText? Bio { get; set; }

    /// <summary>"Kitchens", "Small flats", "Segun furniture" — what to book them for.</summary>
    public List<string> Specialities { get; set; } = [];

    /// <summary>Hidden from the booking page when false. Existing bookings stand.</summary>
    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    /// <summary>The services they offer.</summary>
    public ICollection<ConsultantService> Services { get; set; } = [];

    /// <summary>Their ordinary week.</summary>
    public ICollection<AvailabilityRule> AvailabilityRules { get; set; } = [];

    /// <summary>The days that are not ordinary.</summary>
    public ICollection<AvailabilityException> AvailabilityExceptions { get; set; } = [];
}

/// <summary>
/// One window in a consultant's ordinary week: Sunday, 10:00 to 17:00, in
/// half-hours.
/// </summary>
/// <remarks>
/// <para>
/// <b>Times are Dhaka's, stored as times rather than instants.</b> "Ten in the
/// morning" is not a moment; it is a moment on a given date, and storing it as
/// one would make the rule wrong for every other date. The slot generator
/// combines the date and the time in Dhaka and converts the result to UTC —
/// which is also the only order that survives a server in another time zone.
/// </para>
/// <para>
/// More than one row per day is allowed and is the point: 10:00–13:00 and
/// 15:00–18:00 is a lunch break, expressed by the gap between two rules rather
/// than by a third field nobody would remember to fill in.
/// </para>
/// </remarks>
public class AvailabilityRule : BaseEntity
{
    public long ConsultantId { get; set; }

    public Consultant Consultant { get; set; } = null!;

    /// <summary>
    /// The day of the week this window falls on.
    /// </summary>
    /// <remarks>
    /// Friday and Saturday are the weekend here, and nothing in the code
    /// assumes otherwise — the shop says which days it works by which rows it
    /// writes. PLAN.md §4 calls this out because a Saturday–Sunday assumption
    /// is the classic import from elsewhere.
    /// </remarks>
    public DayOfWeek DayOfWeek { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }

    /// <summary>
    /// How far apart the offered starting times are.
    /// </summary>
    /// <remarks>
    /// Separate from the service's duration on purpose. A ninety-minute site
    /// visit offered on the half-hour gives a customer three times as many
    /// times to choose from as one offered every ninety minutes, and the shop
    /// decides which it wants.
    /// </remarks>
    public int SlotMinutes { get; set; } = 30;
}

/// <summary>
/// A day that is not the ordinary week: a holiday, leave, or a half day.
/// </summary>
/// <remarks>
/// Overrides the rules for that one date entirely. Eid, Pohela Boishakh and a
/// consultant's own leave are the same shape of fact, and a shop that has to
/// edit its weekly rules to take a Thursday off will forget to put them back.
/// </remarks>
public class AvailabilityException : BaseEntity
{
    public long ConsultantId { get; set; }

    public Consultant Consultant { get; set; } = null!;

    /// <summary>The Dhaka date this applies to.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Nothing at all that day. The two times are then ignored.</summary>
    public bool IsClosed { get; set; } = true;

    /// <summary>A replacement window, when it is a half day rather than a day off.</summary>
    public TimeOnly? StartTime { get; set; }

    public TimeOnly? EndTime { get; set; }

    /// <summary>"Eid-ul-Fitr", "Site visit in Chattogram" — for whoever reads the calendar.</summary>
    public string? Note { get; set; }
}
