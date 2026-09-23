using WoodHeart.Domain.Entity.Consultations;
using WoodHeart.Domain.Enums.Consultations;
using WoodHeart.Repository;
using WoodHeart.Service.Services.Consultations;
using WoodHeart.Service.DTOs.Consultations;
using WoodHeart.Service.DTOs.Ordering;

namespace WoodHeart.Service.Interfaces.Consultations;

/// <summary>
/// What is on offer, and when it can happen.
/// </summary>
/// <remarks>
/// The deciding is <c>SlotGenerator</c>'s and stays there. This service fetches
/// the schedule and the diary and hands both to it, so that the calendar the
/// customer sees and the check made when they press Book run the same function
/// over the same inputs.
/// </remarks>
public interface IAvailabilityService
{
    Task<GeneralResponse<IReadOnlyList<ConsultationServiceDto>>> GetServicesAsync(
        CancellationToken cancellationToken = default);

    Task<GeneralResponse<ConsultationServiceDto>> GetServiceBySlugAsync(
        string slug, CancellationToken cancellationToken = default);

    Task<GeneralResponse<IReadOnlyList<ConsultantDto>>> GetConsultantsAsync(
        long? serviceId = null, CancellationToken cancellationToken = default);

    Task<GeneralResponse<AvailabilityDto>> GetAvailabilityAsync(
        AvailabilityQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every free time for a service between two Dhaka dates, by consultant.
    /// </summary>
    /// <remarks>
    /// On the interface rather than kept private, because placement calls it:
    /// the calendar a customer sees and the check made when they press Book
    /// have to be the same computation over the same inputs, and two of them
    /// is how somebody is offered a time that is then refused.
    /// </remarks>
    Task<IReadOnlyList<ConsultantSlots>> ResolveAsync(
        ConsultationService service,
        long? consultantId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);
}

/// <summary>Booking an appointment, and what happens to it afterwards.</summary>
public interface IBookingService
{
    /// <summary>
    /// Books a slot, or says why it could not be booked.
    /// </summary>
    /// <remarks>
    /// The availability check and the write are one transaction, and the
    /// database's unique index is what settles the race the check cannot.
    /// </remarks>
    Task<GeneralResponse<BookingDto>> CreateAsync(
        CreateBookingDto dto,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>One booking, for whoever owns it — by number plus phone for a guest.</summary>
    Task<GeneralResponse<BookingDto>> GetAsync(
        string bookingNumber, string? contactPhone, CancellationToken cancellationToken = default);

    /// <summary>
    /// The same booking for staff: no ownership check, and the phone number
    /// unmasked.
    /// </summary>
    /// <remarks>
    /// Its own method rather than a flag on the one above, because "may this
    /// caller see this booking" and "is this caller the shop" are different
    /// questions and a boolean that conflated them would eventually be passed
    /// the wrong way round. The controller's policy is what makes this safe.
    /// </remarks>
    Task<GeneralResponse<BookingDto>> GetForStaffAsync(
        string bookingNumber, CancellationToken cancellationToken = default);

    Task<GeneralResponse<PagedResult<BookingDto>>> GetMineAsync(
        int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>The customer calling it off themselves, while they still may.</summary>
    Task<GeneralResponse<BookingDto>> CancelAsync(
        string bookingNumber,
        string? contactPhone,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>Staff moving it along: confirm, reschedule's sibling, complete, no-show.</summary>
    Task<GeneralResponse<BookingDto>> SetStatusAsync(
        string bookingNumber,
        BookingStatus status,
        string? note,
        CancellationToken cancellationToken = default);
}

/// <summary>Writing what the shop offers and who is free when.</summary>
public interface IConsultationAdminService
{
    Task<GeneralResponse<IReadOnlyList<ConsultationServiceDto>>> GetServicesAsync(
        CancellationToken cancellationToken = default);

    Task<GeneralResponse<ConsultationServiceDto>> SaveServiceAsync(
        long? id, SaveConsultationServiceDto dto, CancellationToken cancellationToken = default);

    Task<GeneralResponse<IReadOnlyList<ConsultantDto>>> GetConsultantsAsync(
        CancellationToken cancellationToken = default);

    Task<GeneralResponse<ConsultantScheduleDto>> GetConsultantAsync(
        long id, CancellationToken cancellationToken = default);

    Task<GeneralResponse<ConsultantScheduleDto>> SaveConsultantAsync(
        long? id, SaveConsultantDto dto, CancellationToken cancellationToken = default);

    /// <summary>Replaces a consultant's rules and exceptions wholesale.</summary>
    Task<GeneralResponse<ConsultantScheduleDto>> SaveScheduleAsync(
        long consultantId, SaveScheduleDto dto, CancellationToken cancellationToken = default);
}
