using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Identity;
using WoodHeart.Domain.Enums.Consultations;
using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Domain.Entity.Consultations;

/// <summary>
/// Somebody's appointment.
/// </summary>
/// <remarks>
/// <para>
/// <b>A booking is a snapshot, like an order.</b> The fee, the duration and
/// the service's name are copied onto it, because a consultation booked in
/// September at 2,000৳ must still read as 2,000৳ after the shop raises its
/// price in October. The same rule, for the same reason, as an order line.
/// </para>
/// <para>
/// <b>A guest booking is an ordinary booking.</b> <see cref="CustomerId"/> is
/// null and the contact details stand on their own, exactly as on an order —
/// somebody who wants a consultant to look at their flat should not have to
/// make an account first, and the phone number is what links the booking to
/// an account made later.
/// </para>
/// <para>
/// <b><see cref="ScheduledAtUtc"/> is a UTC instant and
/// <see cref="ConsultantId"/> is part of a unique index with it.</b> That index
/// is what actually stops two customers being given the same four o'clock; an
/// application-level check loses the race under load, and losing it means two
/// people in the studio at the same time. See the configuration.
/// </para>
/// </remarks>
public class Booking : BaseEntity
{
    /// <summary>The number quoted down the phone: <c>WHC-2609-00042</c>.</summary>
    public string BookingNumber { get; set; } = null!;

    public long ConsultationServiceId { get; set; }

    public ConsultationService ConsultationService { get; set; } = null!;

    /// <summary>
    /// Null when the customer asked for "anybody".
    /// </summary>
    /// <remarks>
    /// Kept nullable rather than resolved at booking time to whichever
    /// consultant is free: the shop may want to decide who goes on a site visit
    /// after seeing the brief, and a booking that silently picked somebody
    /// would have to be un-picked by hand.
    /// </remarks>
    public long? ConsultantId { get; set; }

    public Consultant? Consultant { get; set; }

    /// <summary>Set when the customer had an account. Null for a guest booking.</summary>
    public long? CustomerId { get; set; }

    public AppUser? Customer { get; set; }

    // --- Contact, snapshotted -----------------------------------------------

    public string ContactName { get; set; } = null!;

    /// <summary>E.164, normalised — <c>+8801712345678</c>.</summary>
    public string ContactPhone { get; set; } = null!;

    public string? ContactEmail { get; set; }

    /// <summary>Which language to write to this customer in. Snapshotted, like an order's.</summary>
    public string CustomerLanguage { get; set; } = GlobalConstants.DefaultLanguage;

    // --- When ----------------------------------------------------------------

    /// <summary>The start of the appointment, in UTC. Presented in Dhaka time.</summary>
    public DateTimeOffset ScheduledAtUtc { get; set; }

    /// <summary>How long it was booked for, copied from the service.</summary>
    public int DurationMinutes { get; set; }

    /// <summary>Where it was before it was moved, so "rescheduled from" reads correctly.</summary>
    public DateTimeOffset? PreviousScheduledAtUtc { get; set; }

    // --- What it is about ----------------------------------------------------

    /// <summary>Where the consultant is going. Required for a site visit, absent otherwise.</summary>
    public DeliveryAddress? SiteAddress { get; set; }

    /// <summary>What the customer wants, in their own words.</summary>
    public string? ProjectBrief { get; set; }

    /// <summary>
    /// What they are prepared to spend, as a band rather than a figure.
    /// </summary>
    /// <remarks>
    /// A band because nobody knows their budget to the taka before the first
    /// conversation, and an empty box gets skipped. It is free text rather than
    /// an enum: the useful bands are a commercial decision (PLAN.md §16), and
    /// hard-coding them now would mean a migration when the shop disagrees.
    /// </remarks>
    public string? BudgetRange { get; set; }

    /// <summary>"Bedroom", "Kitchen" — what the consultation is about.</summary>
    public List<string> RoomTypes { get; set; } = [];

    // --- Money, snapshotted ---------------------------------------------------

    public string Currency { get; set; } = Money.Bdt;

    /// <summary>The fee as it stood when this was booked.</summary>
    public Money Fee { get; set; } = null!;

    /// <summary>
    /// What should be collected up front, or null when nothing should.
    /// </summary>
    /// <remarks>
    /// Recorded rather than charged: taking it needs a gateway, which is Phase
    /// 5. It is on the booking now so that the day deposits are switched on, a
    /// year of bookings does not have to be reinterpreted — and so the shop can
    /// already see what it meant to ask for.
    /// </remarks>
    public Money? AdvanceDue { get; set; }

    // --- Status ----------------------------------------------------------------

    public BookingStatus Status { get; set; } = BookingStatus.Requested;

    /// <summary>Staff-only. Never shown to the customer.</summary>
    public string? InternalNotes { get; set; }

    /// <summary>
    /// The client's key for this booking attempt.
    /// </summary>
    /// <remarks>
    /// The same guard as an order's: somebody on a slow connection presses
    /// "Book" twice, and the unique index turns the second attempt into a
    /// lookup of the first booking rather than a second afternoon blocked out.
    /// </remarks>
    public string? IdempotencyKey { get; set; }

    public ICollection<BookingTimelineEntry> Timeline { get; set; } = [];

    /// <summary>The end of the appointment. Not stored; it follows from the two fields above.</summary>
    public DateTimeOffset EndsAtUtc => ScheduledAtUtc.AddMinutes(DurationMinutes);
}

/// <summary>
/// One entry in a booking's history: who moved it, when, from where to where.
/// </summary>
/// <remarks>
/// Append-only, and the same shape as an order's timeline for the same reason:
/// "who cancelled this" is a question that gets asked, and the actor's name is
/// copied rather than joined so that a consultant who leaves does not erase
/// their name from the record of what they did.
/// </remarks>
public class BookingTimelineEntry : BaseEntity
{
    public long BookingId { get; set; }

    public Booking Booking { get; set; } = null!;

    /// <summary>Null on the first entry — the booking came from nowhere.</summary>
    public BookingStatus? FromStatus { get; set; }

    public BookingStatus ToStatus { get; set; }

    /// <summary>Null when the customer or a background job did it.</summary>
    public long? ActorUserId { get; set; }

    /// <summary>"Rakib (admin)", "Customer", "System" — readable a year later.</summary>
    public string ActorName { get; set; } = null!;

    public string? Note { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
