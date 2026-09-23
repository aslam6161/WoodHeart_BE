using WoodHeart.Domain.Enums.Consultations;
using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Domain.Entity.Consultations;

/// <summary>
/// Something the shop sells an hour of: a studio appointment, a site visit, a
/// call.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a product with a calendar instead of a shelf.</b> It has a name,
/// a price and a page, and the thing that makes it different is that supply is
/// somebody's afternoon rather than a count in a warehouse — which is why
/// duration and buffers live here rather than on the consultant. A site visit
/// takes a morning whoever does it.
/// </para>
/// <para>
/// PLAN.md §16.4 leaves the commercials open. A fee of zero is allowed and is
/// the assumption until the shop says otherwise: a free first consultation
/// that converts into a wardrobe is a perfectly good business, and the model
/// must not force a price on it.
/// </para>
/// </remarks>
public class ConsultationService : SoftDeletableEntity
{
    public LocalizedText Name { get; set; } = null!;

    /// <summary>The URL this is reached by: <c>/consultations/site-visit</c>.</summary>
    public Slug Slug { get; set; } = null!;

    public LocalizedText? Description { get; set; }

    public ConsultationMode Mode { get; set; } = ConsultationMode.InStudio;

    /// <summary>How long the appointment itself is.</summary>
    public int DurationMinutes { get; set; } = 60;

    /// <summary>Zero is a free consultation, and that is a real option.</summary>
    public Money Fee { get; set; } = null!;

    /// <summary>
    /// Whether money has to be taken before the slot is held.
    /// </summary>
    /// <remarks>
    /// <b>Recorded, not yet enforced.</b> Taking a deposit needs a gateway, and
    /// that is Phase 5. Until then this and <see cref="AdvanceAmount"/> are
    /// snapshotted onto the booking so that switching it on later does not have
    /// to reinterpret what the old bookings meant — and the shop can already
    /// see, on the booking, what it should have collected.
    /// </remarks>
    public bool RequiresAdvance { get; set; }

    /// <summary>What the deposit is. Null means the whole fee.</summary>
    public Money? AdvanceAmount { get; set; }

    /// <summary>
    /// Time kept clear before the appointment.
    /// </summary>
    /// <remarks>
    /// The consultant's journey, or the ten minutes it takes to lay a room out
    /// again after the last customer. Offering a slot that starts the moment
    /// the previous one ends is how a day that looks fine on the calendar
    /// becomes an afternoon of apologies.
    /// </remarks>
    public int BufferBeforeMinutes { get; set; }

    /// <summary>The same, afterwards.</summary>
    public int BufferAfterMinutes { get; set; }

    /// <summary>Hidden from the booking page when false. Existing bookings stand.</summary>
    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    /// <summary>The consultants who offer it.</summary>
    public ICollection<ConsultantService> Consultants { get; set; } = [];

    /// <summary>
    /// What this booking should collect up front, given the fee.
    /// </summary>
    /// <remarks>
    /// Here rather than in a service because two callers need the same answer —
    /// the booking page quoting it and the placement snapshotting it — and a
    /// second copy of "is it the whole fee or the named amount" is a second
    /// answer.
    /// </remarks>
    public Money? AdvanceDue() =>
        !RequiresAdvance ? null : AdvanceAmount ?? Fee;
}

/// <summary>
/// One consultant offering one service.
/// </summary>
/// <remarks>
/// A join row rather than a list on either side: "who can do a site visit" and
/// "what does Rakib do" are both ordinary questions, and a denormalised array
/// on one of them answers only its own.
/// </remarks>
public class ConsultantService : BaseEntity
{
    public long ConsultantId { get; set; }

    public Consultant Consultant { get; set; } = null!;

    public long ConsultationServiceId { get; set; }

    public ConsultationService ConsultationService { get; set; } = null!;
}
