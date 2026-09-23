using WoodHeart.Domain.Entity.Consultations;

namespace WoodHeart.Repository.Interfaces.Consultations;

/// <summary>What the shop sells an hour of. Stages changes; does not commit them.</summary>
public interface IConsultationServiceRepository : IRepository<ConsultationService>
{
    /// <summary>Everything on offer, in the shop's own order, with its consultants.</summary>
    Task<IReadOnlyList<ConsultationService>> GetActiveAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Every one, active or not, for the admin list.</summary>
    Task<IReadOnlyList<ConsultationService>> GetAllForAdminAsync(
        CancellationToken cancellationToken = default);

    Task<ConsultationService?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>One service with the consultants who offer it, for the edit screen.</summary>
    Task<ConsultationService?> GetWithConsultantsAsync(
        long id, CancellationToken cancellationToken = default);

    Task<bool> SlugExistsAsync(
        string slug, long? excludeId = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// The people whose time is being sold, and their diaries.
/// </summary>
/// <remarks>
/// <see cref="GetForServiceAsync"/> loads the rules and the exceptions with the
/// consultant, because the availability endpoint needs all three and fetching
/// them separately is the N+1 on the page that decides whether a customer
/// books at all.
/// </remarks>
public interface IConsultantRepository : IRepository<Consultant>
{
    Task<IReadOnlyList<Consultant>> GetActiveAsync(
        long? serviceId = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Consultant>> GetAllForAdminAsync(CancellationToken cancellationToken = default);

    /// <summary>One consultant with their services, rules and exceptions.</summary>
    Task<Consultant?> GetWithScheduleAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The active consultants who offer a service, with their whole diary
    /// loaded, for generating slots.
    /// </summary>
    Task<IReadOnlyList<Consultant>> GetForServiceAsync(
        long serviceId, CancellationToken cancellationToken = default);
}

/// <summary>Appointments. Stages changes; does not commit them.</summary>
public interface IBookingRepository : IRepository<Booking>
{
    Task<Booking?> GetByNumberAsync(string bookingNumber, CancellationToken cancellationToken = default);

    Task<Booking?> GetDetailAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The booking already written for this key, if there is one.
    /// </summary>
    /// <remarks>
    /// The first half of the double-tap guard, as on checkout. The other half
    /// is the unique index, which catches the two requests that arrive close
    /// enough together for this lookup to miss.
    /// </remarks>
    Task<Booking?> GetByIdempotencyKeyAsync(
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// The appointments still holding a slot, for a set of consultants, over a
    /// window of time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Filtered to the statuses <c>BookingStatusMachine.HoldsTheSlot</c> names:
    /// a cancelled booking must not keep an afternoon blocked.
    /// </para>
    /// <para>
    /// Bookings with no consultant — "anybody" — are included with a null id,
    /// because they still occupy the shop even before somebody is assigned.
    /// The caller decides what that means for each consultant.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<Booking>> GetHeldBetweenAsync(
        IReadOnlyCollection<long> consultantIds,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);

    /// <summary>A customer's bookings, soonest first.</summary>
    Task<IReadOnlyList<Booking>> GetForCustomerAsync(
        long customerId, int skip, int take, CancellationToken cancellationToken = default);

    Task<int> CountForCustomerAsync(long customerId, CancellationToken cancellationToken = default);
}
