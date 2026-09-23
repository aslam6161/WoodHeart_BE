namespace WoodHeart.Domain.Constants;

/// <summary>
/// Stable error codes for consultations and bookings.
/// </summary>
/// <remarks>
/// Same contract as <see cref="OrderingErrors"/>: the client branches on the
/// code, the message is prose, and the suffix picks the HTTP status.
/// </remarks>
public static class ConsultationErrors
{
    private const string Prefix = "consultations.";

    // --- Booking --------------------------------------------------------------

    public const string ServiceNotFound = Prefix + "service.not_found";

    public const string ConsultantNotFound = Prefix + "consultant.not_found";

    /// <summary>The consultant does not offer this service.</summary>
    public const string ConsultantNotOffered = Prefix + "consultant_not_offered";

    /// <summary>
    /// The chosen time is not one the schedule offers — outside the working
    /// week, on a holiday, in the past, or not on the half hour.
    /// </summary>
    public const string SlotNotAvailable = Prefix + "slot_not_available";

    /// <summary>
    /// Somebody booked it while this customer was filling in the form.
    /// </summary>
    /// <remarks>
    /// A conflict rather than a validation failure: the request was correct and
    /// the world moved. It is also what the unique index produces when two
    /// requests arrive close enough together for the availability check to miss
    /// — which is the case that matters, because losing it means two people in
    /// the studio at four o'clock.
    /// </remarks>
    public const string SlotTaken = Prefix + "slot_taken.conflict";

    /// <summary>A site visit with nowhere to visit.</summary>
    public const string SiteAddressRequired = Prefix + "site_address_required";

    public const string ContactPhoneInvalid = Prefix + "contact_phone_invalid";

    /// <summary>Asking for a range of dates longer than the API will compute.</summary>
    public const string RangeTooLong = Prefix + "range_too_long";

    // --- A booking that exists -------------------------------------------------

    public const string BookingNotFound = Prefix + "booking.not_found";

    /// <summary>
    /// The booking exists, but not for whoever is asking.
    /// </summary>
    /// <remarks>
    /// Returned as a not-found rather than a forbidden, so the endpoint cannot
    /// be used to discover which booking numbers exist.
    /// </remarks>
    public const string BookingNotYours = Prefix + "booking.not_found";

    /// <summary>The move is not one <c>BookingStatusMachine</c> allows from here.</summary>
    public const string TransitionInvalid = Prefix + "booking.transition_invalid.conflict";

    /// <summary>Past the point where a customer can call it off themselves.</summary>
    public const string NotCancellable = Prefix + "booking.not_cancellable.conflict";

    // --- Managing the schedule -------------------------------------------------

    public const string NameRequired = Prefix + "name_required";

    /// <summary>A duration or a slot size that is not a number of minutes.</summary>
    public const string DurationInvalid = Prefix + "duration_invalid";

    /// <summary>A window that ends before it starts, or is empty.</summary>
    public const string WindowInvalid = Prefix + "window_invalid";

    public const string SlugTaken = Prefix + "slug_taken";
}
