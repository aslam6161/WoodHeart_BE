using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Consultations;
using WoodHeart.Domain.Entity.Consultations;
using WoodHeart.Domain.Helpers;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Consultations;
using WoodHeart.Service.DTOs.Consultations;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Consultations;
using WoodHeart.Service.Mapping.Consultations;

namespace WoodHeart.Service.Services.Consultations;

/// <summary>
/// One consultant's free times, and who they belong to.
/// </summary>
/// <param name="ConsultantId">Whose afternoon it is.</param>
public readonly record struct ConsultantSlots(
    long ConsultantId, string ConsultantName, IReadOnlyList<DateTimeOffset> Slots);

/// <inheritdoc cref="IAvailabilityService" />
public class AvailabilityService(
    IConsultationServiceRepository services,
    IConsultantRepository consultants,
    IBookingRepository bookings,
    ICurrentUserService currentUser,
    IDateTimeProvider clock) : IAvailabilityService
{
    public async Task<GeneralResponse<IReadOnlyList<ConsultationServiceDto>>> GetServicesAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await services.GetActiveAsync(cancellationToken);

        return GeneralResponse<IReadOnlyList<ConsultationServiceDto>>.Success(
            [.. rows.Select(row => ConsultationMapper.ToDto(row, currentUser.Language))]);
    }

    public async Task<GeneralResponse<ConsultationServiceDto>> GetServiceBySlugAsync(
        string slug, CancellationToken cancellationToken = default)
    {
        var service = await services.GetBySlugAsync(slug, cancellationToken);

        return service is null || !service.IsActive
            ? GeneralResponse<ConsultationServiceDto>.Fail(
                ConsultationErrors.ServiceNotFound, "We do not offer that consultation.")
            : GeneralResponse<ConsultationServiceDto>.Success(
                ConsultationMapper.ToDto(service, currentUser.Language));
    }

    public async Task<GeneralResponse<IReadOnlyList<ConsultantDto>>> GetConsultantsAsync(
        long? serviceId = null, CancellationToken cancellationToken = default)
    {
        var rows = await consultants.GetActiveAsync(serviceId, cancellationToken);

        return GeneralResponse<IReadOnlyList<ConsultantDto>>.Success(
            [.. rows.Select(row => ConsultationMapper.ToDto(row, currentUser.Language))]);
    }

    public async Task<GeneralResponse<AvailabilityDto>> GetAvailabilityAsync(
        AvailabilityQueryDto query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var service = await services.GetByIdAsync(query.ServiceId, cancellationToken);

        if (service is null || !service.IsActive || service.IsDeleted)
        {
            return GeneralResponse<AvailabilityDto>.Fail(
                ConsultationErrors.ServiceNotFound, "We do not offer that consultation.");
        }

        var from = query.From ?? clock.DhakaToday;
        var to = query.To ?? from.AddDays(BookingRules.DefaultHorizonDays);

        if (to < from)
        {
            to = from;
        }

        if (to.DayNumber - from.DayNumber >= SlotGenerator.MaxDays)
        {
            return GeneralResponse<AvailabilityDto>.Fail(
                ConsultationErrors.RangeTooLong,
                $"Please ask for at most {SlotGenerator.MaxDays} days at a time.");
        }

        var perConsultant = await ResolveAsync(service, query.ConsultantId, from, to, cancellationToken);

        return GeneralResponse<AvailabilityDto>.Success(new AvailabilityDto
        {
            ServiceId = service.Id,
            DurationMinutes = service.DurationMinutes,
            Days = Group(perConsultant)
        });
    }

    /// <summary>
    /// Every free time for this service between two dates, by consultant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Shared with the booking path on purpose: the calendar a customer sees
    /// and the check made when they press Book are the same computation over
    /// the same inputs. Two of them is how a customer is offered a time that
    /// is then refused.
    /// </para>
    /// <para>
    /// A booking with no consultant assigned blocks everybody, because until
    /// somebody is put against it the shop does not know whose afternoon it
    /// is. In practice placement always assigns one, so this is the belt to
    /// that braces.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<ConsultantSlots>> ResolveAsync(
        ConsultationService service,
        long? consultantId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var offering = await consultants.GetForServiceAsync(service.Id, cancellationToken);

        if (consultantId is { } wanted)
        {
            offering = [.. offering.Where(candidate => candidate.Id == wanted)];
        }

        if (offering.Count == 0)
        {
            return [];
        }

        // One window wide enough to cover every date asked about, so the diary
        // is read once rather than once per consultant per day.
        var windowStart = SlotGenerator.ToUtc(from, TimeOnly.MinValue);
        var windowEnd = SlotGenerator.ToUtc(to.AddDays(1), TimeOnly.MinValue);

        var held = await bookings.GetHeldBetweenAsync(
            [.. offering.Select(candidate => candidate.Id)], windowStart, windowEnd, cancellationToken);

        var request = new SlotRequest(
            from,
            to,
            service.DurationMinutes,
            service.BufferBeforeMinutes,
            service.BufferAfterMinutes);

        var now = clock.UtcNow;
        var results = new List<ConsultantSlots>();

        foreach (var candidate in offering)
        {
            var taken = held
                .Where(booking => booking.ConsultantId is null || booking.ConsultantId == candidate.Id)
                .Select(booking => new BookedSlot(booking.ScheduledAtUtc, booking.DurationMinutes))
                .ToList();

            var slots = SlotGenerator.Generate(
                request,
                [
                    .. candidate.AvailabilityRules.Select(rule => new AvailabilityWindow(
                        rule.DayOfWeek, rule.StartTime, rule.EndTime, rule.SlotMinutes))
                ],
                [
                    .. candidate.AvailabilityExceptions.Select(entry => new ScheduleException(
                        entry.Date, entry.IsClosed, entry.StartTime, entry.EndTime))
                ],
                taken,
                now);

            if (slots.Count > 0)
            {
                results.Add(new ConsultantSlots(candidate.Id, candidate.Name, slots));
            }
        }

        return results;
    }

    /// <summary>
    /// Folds every consultant's times into one list per Dhaka date.
    /// </summary>
    /// <remarks>
    /// A time offered by two consultants appears once, against the first of
    /// them. The customer asked for a four o'clock, not for a person; when they
    /// did ask for a person, the list only ever held that person.
    /// </remarks>
    private static List<AvailabilityDayDto> Group(IReadOnlyList<ConsultantSlots> perConsultant)
    {
        var byDate = new SortedDictionary<DateOnly, Dictionary<DateTimeOffset, AvailabilitySlotDto>>();

        foreach (var entry in perConsultant)
        {
            foreach (var slot in entry.Slots)
            {
                var dhaka = slot.ToOffset(SlotGenerator.DhakaOffset);
                var date = DateOnly.FromDateTime(dhaka.DateTime);

                if (!byDate.TryGetValue(date, out var slots))
                {
                    slots = [];
                    byDate[date] = slots;
                }

                slots.TryAdd(slot, new AvailabilitySlotDto
                {
                    StartUtc = slot,
                    ConsultantId = entry.ConsultantId,
                    ConsultantName = entry.ConsultantName
                });
            }
        }

        return
        [
            .. byDate.Select(pair => new AvailabilityDayDto
            {
                Date = pair.Key,
                Slots = [.. pair.Value.Values.OrderBy(slot => slot.StartUtc)]
            })
        ];
    }
}
