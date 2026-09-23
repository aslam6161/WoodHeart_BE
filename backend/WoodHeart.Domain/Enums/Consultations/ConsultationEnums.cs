namespace WoodHeart.Domain.Enums.Consultations;

/// <summary>
/// Where the consultation happens.
/// </summary>
/// <remarks>
/// It is not decoration: a site visit costs the shop a journey and blocks far
/// more of a consultant's day than a phone call, which is why the fee, the
/// duration and the buffers are all per service rather than per shop.
/// </remarks>
public enum ConsultationMode
{
    /// <summary>Over a call or a video link.</summary>
    Online,

    /// <summary>The customer comes to the showroom.</summary>
    InStudio,

    /// <summary>The consultant goes to the customer's home.</summary>
    SiteVisit
}

/// <summary>
/// Where a booking is.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Requested"/> rather than Confirmed on submission, deliberately.
/// A slot the customer picked is held, but a consultation is a person's
/// afternoon: somebody at the shop looks at it before it becomes a promise.
/// </para>
/// <para>
/// <see cref="NoShow"/> is separate from <see cref="Cancelled"/> because the
/// difference is money. A cancellation the day before is a slot that could
/// have been resold; somebody who simply did not arrive cost the consultant
/// the whole afternoon, and a shop that cannot tell the two apart cannot
/// decide whether to start asking for deposits.
/// </para>
/// </remarks>
public enum BookingStatus
{
    /// <summary>Submitted by the customer. The slot is held.</summary>
    Requested,

    /// <summary>The shop has agreed to it.</summary>
    Confirmed,

    /// <summary>Moved to another slot. The new time is on the booking.</summary>
    Rescheduled,

    /// <summary>It happened.</summary>
    Completed,

    /// <summary>Called off, by either side.</summary>
    Cancelled,

    /// <summary>The customer did not arrive.</summary>
    NoShow
}
