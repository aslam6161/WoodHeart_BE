using Microsoft.EntityFrameworkCore;
using WoodHeart.Domain.Consultations;
using WoodHeart.Domain.Entity.Consultations;
using WoodHeart.Domain.Enums.Consultations;
using WoodHeart.Repository.Interfaces.Consultations;
using WoodHeart.Repository.Repositories.Catalog;

namespace WoodHeart.Repository.Repositories.Consultations;

public class ConsultationServiceRepository(DataContext context)
    : Repository<ConsultationService>(context), IConsultationServiceRepository
{
    public async Task<IReadOnlyList<ConsultationService>> GetActiveAsync(
        CancellationToken cancellationToken = default) =>
        await WithConsultants()
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ConsultationService>> GetAllForAdminAsync(
        CancellationToken cancellationToken = default) =>
        await WithConsultants()
            .AsNoTracking()
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

    public async Task<ConsultationService?> GetBySlugAsync(
        string slug, CancellationToken cancellationToken = default)
    {
        // A slug that cannot be parsed is not a 500: it is a URL somebody
        // typed, and the answer is that there is no such service.
        if (!SlugQuery.TrySlug(slug, out var target))
        {
            return null;
        }

        return await WithConsultants()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Slug == target, cancellationToken);
    }

    public async Task<ConsultationService?> GetWithConsultantsAsync(
        long id, CancellationToken cancellationToken = default) =>
        await WithConsultants().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<bool> SlugExistsAsync(
        string slug, long? excludeId = null, CancellationToken cancellationToken = default)
    {
        if (!SlugQuery.TrySlug(slug, out var target))
        {
            return false;
        }

        return await Set.AnyAsync(
            x => x.Slug == target && (excludeId == null || x.Id != excludeId),
            cancellationToken);
    }

    private IQueryable<ConsultationService> WithConsultants() =>
        Set.Include(x => x.Consultants).ThenInclude(link => link.Consultant).AsSplitQuery();
}

public class ConsultantRepository(DataContext context)
    : Repository<Consultant>(context), IConsultantRepository
{
    public async Task<IReadOnlyList<Consultant>> GetActiveAsync(
        long? serviceId = null, CancellationToken cancellationToken = default)
    {
        var query = Set.AsNoTracking()
            .Include(x => x.Services)
            .Where(x => x.IsActive);

        if (serviceId is { } id)
        {
            query = query.Where(x => x.Services.Any(link => link.ConsultationServiceId == id));
        }

        return await query
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Consultant>> GetAllForAdminAsync(
        CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking()
            .Include(x => x.Services)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    public async Task<Consultant?> GetWithScheduleAsync(
        long id, CancellationToken cancellationToken = default) =>
        await WithSchedule().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Consultant>> GetForServiceAsync(
        long serviceId, CancellationToken cancellationToken = default) =>
        await WithSchedule()
            .AsNoTracking()
            .Where(x => x.IsActive && x.Services.Any(link => link.ConsultationServiceId == serviceId))
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// A consultant with their whole diary, in one round trip.
    /// </summary>
    /// <remarks>
    /// Split, because three collection includes on one root is the query that
    /// returns the consultant's biography once per rule per exception. The
    /// availability page asks for this on every date change.
    /// </remarks>
    private IQueryable<Consultant> WithSchedule() =>
        Set.Include(x => x.Services)
            .Include(x => x.AvailabilityRules)
            .Include(x => x.AvailabilityExceptions)
            .AsSplitQuery();
}

public class BookingRepository(DataContext context)
    : Repository<Booking>(context), IBookingRepository
{
    public async Task<Booking?> GetByNumberAsync(
        string bookingNumber, CancellationToken cancellationToken = default) =>
        await WithDetail().FirstOrDefaultAsync(x => x.BookingNumber == bookingNumber, cancellationToken);

    public async Task<Booking?> GetDetailAsync(long id, CancellationToken cancellationToken = default) =>
        await WithDetail().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<Booking?> GetByIdempotencyKeyAsync(
        string idempotencyKey, CancellationToken cancellationToken = default) =>
        await WithDetail().FirstOrDefaultAsync(
            x => x.IdempotencyKey == idempotencyKey, cancellationToken);

    public async Task<IReadOnlyList<Booking>> GetHeldBetweenAsync(
        IReadOnlyCollection<long> consultantIds,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        long? excludeBookingId = null,
        CancellationToken cancellationToken = default)
    {
        var ids = consultantIds.Distinct().ToArray();

        return await Set.AsNoTracking()
            .Where(x => Held.Contains(x.Status)
                        && x.ScheduledAtUtc < toUtc
                        && x.ScheduledAtUtc >= fromUtc
                        && (excludeBookingId == null || x.Id != excludeBookingId)
                        && (x.ConsultantId == null || ids.Contains(x.ConsultantId.Value)))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The diary, as the board asked for it.
    /// </summary>
    /// <remarks>
    /// <b>Soonest first.</b> A diary reads forwards: the page staff want when
    /// they open the board is the next few days, not the oldest booking the
    /// shop ever took. The service is what decides that an unfiltered query
    /// starts at today — the repository answers exactly what it was asked.
    /// </remarks>
    public async Task<PagedList<Booking>> SearchAsync(
        BookingSearch criteria, CancellationToken cancellationToken = default)
    {
        var query = Set.AsNoTracking()
            .Include(x => x.ConsultationService)
            .Include(x => x.Consultant)
            .AsQueryable();

        if (criteria.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        if (criteria.ConsultantId is { } consultantId)
        {
            query = query.Where(x => x.ConsultantId == consultantId);
        }

        if (criteria.Mode is { } mode)
        {
            query = query.Where(x => x.ConsultationService.Mode == mode);
        }

        if (criteria.FromUtc is { } from)
        {
            query = query.Where(x => x.ScheduledAtUtc >= from);
        }

        if (criteria.ToUtc is { } to)
        {
            query = query.Where(x => x.ScheduledAtUtc < to);
        }

        if (!string.IsNullOrWhiteSpace(criteria.Term))
        {
            var pattern = $"%{criteria.Term.Trim()}%";

            // The three things somebody has in front of them when they ring:
            // the number on the confirmation, their name, or the phone the
            // booking was made from.
            query = query.Where(x =>
                EF.Functions.ILike(x.BookingNumber, pattern)
                || EF.Functions.ILike(x.ContactName, pattern)
                || EF.Functions.ILike(x.ContactPhone, pattern));
        }

        return await PagedList<Booking>.CreateAsync(
            query.OrderBy(x => x.ScheduledAtUtc).ThenBy(x => x.Id),
            criteria.Page,
            criteria.PageSize,
            cancellationToken);
    }

    /// <summary>
    /// The appointments inside the reminder horizon that still owe a message.
    /// </summary>
    /// <remarks>
    /// Tracked, not <c>AsNoTracking</c>: the caller stamps the booking in the
    /// same unit of work that queues the message, so "sent" and "told" are one
    /// fact. The service is included because the message names it.
    /// </remarks>
    public async Task<IReadOnlyList<Booking>> GetDueForReminderAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int take,
        CancellationToken cancellationToken = default) =>
        await Set
            .Include(x => x.ConsultationService)
            .Where(x => BookingReminders.Remindable.Contains(x.Status)
                        && x.ScheduledAtUtc > fromUtc
                        && x.ScheduledAtUtc <= toUtc
                        && (x.FirstReminderSentAt == null || x.FinalReminderSentAt == null))
            .OrderBy(x => x.ScheduledAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Booking>> GetForCustomerAsync(
        long customerId, int skip, int take, CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking()
            .Include(x => x.ConsultationService)
            .Where(x => x.CustomerId == customerId)
            .OrderByDescending(x => x.ScheduledAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<int> CountForCustomerAsync(
        long customerId, CancellationToken cancellationToken = default) =>
        await Set.CountAsync(x => x.CustomerId == customerId, cancellationToken);

    /// <summary>
    /// The statuses that occupy a diary.
    /// </summary>
    /// <remarks>
    /// Spelled out rather than calling <c>BookingStatusMachine.HoldsTheSlot</c>,
    /// because EF cannot translate a method call into SQL. The test that keeps
    /// the two in step lives beside the status machine — if they drift, the
    /// availability page and the unique index stop agreeing about what "taken"
    /// means.
    /// </remarks>
    private static readonly BookingStatus[] Held =
    [
        BookingStatus.Requested,
        BookingStatus.Confirmed,
        BookingStatus.Rescheduled
    ];

    private IQueryable<Booking> WithDetail() =>
        Set.Include(x => x.ConsultationService)
            .Include(x => x.Consultant)
            .Include(x => x.Timeline.OrderBy(entry => entry.OccurredAt).ThenBy(entry => entry.Id))
            .AsSplitQuery();
}
