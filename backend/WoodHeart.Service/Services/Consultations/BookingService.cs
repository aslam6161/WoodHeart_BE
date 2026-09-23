using System.Text.Json;
using Microsoft.Extensions.Logging;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Consultations;
using WoodHeart.Domain.Entity.Consultations;
using WoodHeart.Domain.Enums.Consultations;
using WoodHeart.Domain.Helpers;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Consultations;
using WoodHeart.Service.DTOs.Consultations;
using WoodHeart.Service.DTOs.Ordering;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Consultations;
using WoodHeart.Service.Interfaces.Notifications;
using WoodHeart.Service.Mapping.Consultations;
using WoodHeart.Service.Services.Common;
using WoodHeart.Service.Services.Notifications;

namespace WoodHeart.Service.Services.Consultations;

/// <summary>
/// Booking an appointment, and what happens to it afterwards.
/// </summary>
/// <remarks>
/// <para>
/// <b>The slot is checked with the same function that drew the calendar.</b>
/// A client sends a time, not an availability decision, and the time is
/// re-derived here from the schedule and the diary — so a request naming
/// midnight on a Friday is refused however it was constructed.
/// </para>
/// <para>
/// <b>The check still loses a race, and the database settles it.</b> Two
/// requests can both read "four o'clock is free" before either writes. The
/// unique index on the consultant and the slot is what cannot lose, and the
/// second write comes back as a conflict rather than as two people in the
/// studio at once.
/// </para>
/// <para>
/// <b>A guest booking is the main path.</b> Somebody who wants a consultant to
/// look at their flat should not have to make an account first, so the contact
/// details stand on their own and the phone number is what links the booking to
/// an account made later — the same rule as an order.
/// </para>
/// </remarks>
public class BookingService(
    IBookingRepository bookings,
    IConsultationServiceRepository services,
    IAvailabilityService availability,
    INumberSequenceService numbers,
    INotificationQueue notifications,
    ICurrentUserService currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork,
    ILogger<BookingService> logger) : IBookingService
{
    public async Task<GeneralResponse<BookingDto>> CreateAsync(
        CreateBookingDto dto,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        // The same guard checkout has: a customer on a slow connection presses
        // Book twice, and the second press must find the first booking rather
        // than block a second afternoon.
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await bookings.GetByIdempotencyKeyAsync(idempotencyKey, cancellationToken);

            if (existing is not null)
            {
                return Ok(existing);
            }
        }

        if (!PhoneNumber.TryParse(dto.ContactPhone, out var phone) || phone is null)
        {
            return Fail(
                ConsultationErrors.ContactPhoneInvalid,
                "That does not look like a Bangladeshi mobile number.");
        }

        var service = await services.GetByIdAsync(dto.ServiceId, cancellationToken);

        if (service is null || !service.IsActive || service.IsDeleted)
        {
            return Fail(ConsultationErrors.ServiceNotFound, "We do not offer that consultation.");
        }

        DeliveryAddress? site = null;

        if (service.Mode == ConsultationMode.SiteVisit)
        {
            if (dto.SiteAddress is null)
            {
                return Fail(
                    ConsultationErrors.SiteAddressRequired,
                    "A site visit needs the address we are visiting.");
            }

            try
            {
                site = ToAddress(dto.SiteAddress);
            }
            catch (ArgumentException ex)
            {
                return Fail(ConsultationErrors.SiteAddressRequired, ex.Message);
            }
        }

        return await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var date = DateOnly.FromDateTime(
                dto.StartUtc.ToOffset(SlotGenerator.DhakaOffset).DateTime);

            var free = await availability.ResolveAsync(
                service, dto.ConsultantId, date, date, excludeBookingId: null, ct);

            // Whoever is free at exactly this time. When the customer named a
            // consultant the list only ever held that one, so the same line
            // answers both cases — and when they asked for anybody, somebody
            // is assigned here rather than left null, because an unassigned
            // booking is nobody's afternoon and the unique index cannot
            // protect it.
            var chosen = free
                .Where(entry => entry.Slots.Contains(dto.StartUtc))
                .Cast<ConsultantSlots?>()
                .FirstOrDefault();

            if (chosen is not { } assigned)
            {
                return Fail(
                    ConsultationErrors.SlotNotAvailable,
                    "That time is no longer free. Please choose another.");
            }

            var booking = Build(dto, service, assigned.ConsultantId, phone, site);

            booking.BookingNumber = await numbers.NextAsync(
                NumberSequenceService.Bookings,
                GlobalConstants.DefaultBookingNumberPrefix,
                ct);

            booking.IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey;

            Record(booking, from: null, to: BookingStatus.Requested, "Booking requested.");

            await bookings.InsertAsync(booking, ct);
            await QueueAsync(booking, service, "booking.requested", at: null, ct);

            await unitOfWork.SaveChangesAsync(ct);

            ConsultationLog.BookingRequested(
                logger, booking.BookingNumber, booking.ScheduledAtUtc, assigned.ConsultantName);

            // Re-read so the answer carries the service and consultant names
            // the customer is about to be shown.
            var saved = await bookings.GetDetailAsync(booking.Id, ct);

            return GeneralResponse<BookingDto>.Success(
                ConsultationMapper.ToDto(saved ?? booking, currentUser.Language), id: booking.Id);
        }, cancellationToken);
    }

    public async Task<GeneralResponse<BookingDto>> GetAsync(
        string bookingNumber, string? contactPhone, CancellationToken cancellationToken = default)
    {
        var booking = await bookings.GetByNumberAsync(bookingNumber, cancellationToken);

        return booking is null || !MayView(booking, contactPhone)
            ? Fail(ConsultationErrors.BookingNotFound, "We could not find that booking.")
            : Ok(booking);
    }

    public async Task<GeneralResponse<BookingDto>> GetForStaffAsync(
        string bookingNumber, CancellationToken cancellationToken = default)
    {
        var booking = await bookings.GetByNumberAsync(bookingNumber, cancellationToken);

        return booking is null
            ? Fail(ConsultationErrors.BookingNotFound, "We could not find that booking.")
            : GeneralResponse<BookingDto>.Success(
                ConsultationMapper.ToDto(booking, currentUser.Language, maskPhone: false),
                id: booking.Id);
    }

    public async Task<GeneralResponse<PagedResult<BookingDto>>> GetMineAsync(
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } customerId)
        {
            return GeneralResponse<PagedResult<BookingDto>>.Fail(
                ConsultationErrors.BookingNotFound, "Please sign in to see your bookings.");
        }

        var size = Math.Clamp(pageSize, 1, BookingRules.MaxPageSize);
        var current = Math.Max(page, 1);

        var rows = await bookings.GetForCustomerAsync(
            customerId, (current - 1) * size, size, cancellationToken);

        var total = await bookings.CountForCustomerAsync(customerId, cancellationToken);

        return GeneralResponse<PagedResult<BookingDto>>.Success(new PagedResult<BookingDto>
        {
            Items = [.. rows.Select(row => ConsultationMapper.ToDto(row, currentUser.Language))],
            Total = total,
            Page = current,
            PageSize = size
        });
    }

    /// <inheritdoc />
    /// <remarks>
    /// <b>An unfiltered board opens on today.</b> Staff open it to see what is
    /// coming, and a first page showing the oldest booking the shop ever took
    /// is no use to anybody. A term is the exception: somebody hunting one
    /// booking is not browsing the diary, and the booking they want may well
    /// be last month's.
    /// </remarks>
    public async Task<GeneralResponse<PagedResult<BookingListItemDto>>> SearchAsync(
        BookingQueryDto query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var browsing = string.IsNullOrWhiteSpace(query.Term) && query.To is null;
        var from = query.From ?? (browsing ? clock.DhakaToday : (DateOnly?)null);

        var criteria = new BookingSearch(
            query.Term,
            query.Status,
            query.ConsultantId,
            query.Mode,

            // Dhaka dates in, UTC instants out. "Bookings on the 27th" means
            // the 27th as the shop lives it, and the stored column is UTC.
            from is { } start ? SlotGenerator.ToUtc(start, TimeOnly.MinValue) : null,
            query.To is { } end ? SlotGenerator.ToUtc(end.AddDays(1), TimeOnly.MinValue) : null,

            Math.Max(query.Page, 1),
            Math.Clamp(query.PageSize, 1, BookingRules.MaxPageSize));

        var page = await bookings.SearchAsync(criteria, cancellationToken);

        return GeneralResponse<PagedResult<BookingListItemDto>>.Success(
            new PagedResult<BookingListItemDto>
            {
                Items =
                [
                    .. page.Select(booking =>
                        ConsultationMapper.ToListItem(booking, currentUser.Language))
                ],
                Total = page.TotalCount,
                Page = page.CurrentPage,
                PageSize = page.PageSize
            });
    }

    public async Task<GeneralResponse<BookingDto>> CancelAsync(
        string bookingNumber,
        string? contactPhone,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var booking = await bookings.GetByNumberAsync(bookingNumber, cancellationToken);

        if (booking is null || !MayView(booking, contactPhone))
        {
            return Fail(ConsultationErrors.BookingNotFound, "We could not find that booking.");
        }

        if (!BookingStatusMachine.IsCustomerCancellable(booking.Status))
        {
            return Fail(
                ConsultationErrors.NotCancellable,
                "That booking can no longer be cancelled here. Please telephone us.");
        }

        return await MoveAsync(
            booking,
            BookingStatus.Cancelled,
            reason,
            currentUser.UserId is null ? "Customer" : currentUser.PhoneNumber ?? "Customer",
            cancellationToken);
    }

    public async Task<GeneralResponse<BookingDto>> SetStatusAsync(
        string bookingNumber,
        BookingStatus status,
        string? note,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(status))
        {
            return Fail(ConsultationErrors.TransitionInvalid, "That is not a status a booking can be in.");
        }

        var booking = await bookings.GetByNumberAsync(bookingNumber, cancellationToken);

        if (booking is null)
        {
            return Fail(ConsultationErrors.BookingNotFound, "We could not find that booking.");
        }

        return await MoveAsync(
            booking,
            status,
            note,
            currentUser.PhoneNumber ?? "Staff",
            cancellationToken,
            maskPhone: false);
    }

    /// <inheritdoc />
    public async Task<GeneralResponse<BookingDto>> RescheduleAsync(
        string bookingNumber,
        DateTimeOffset startUtc,
        long? consultantId,
        string? note,
        CancellationToken cancellationToken = default)
    {
        var booking = await bookings.GetByNumberAsync(bookingNumber, cancellationToken);

        if (booking is null)
        {
            return Fail(ConsultationErrors.BookingNotFound, "We could not find that booking.");
        }

        if (!BookingStatusMachine.CanTransition(booking.Status, BookingStatus.Rescheduled))
        {
            return Fail(
                ConsultationErrors.TransitionInvalid,
                $"A {booking.Status} booking cannot be moved.");
        }

        var target = consultantId ?? booking.ConsultantId;

        // Refused rather than ignored: a no-op move still writes a timeline
        // entry and sends "we have moved you" about a move that did not
        // happen, which is how somebody is told twice and turns up once.
        if (booking.ScheduledAtUtc == startUtc && target == booking.ConsultantId)
        {
            return Fail(
                ConsultationErrors.SlotUnchanged, "That booking is already at that time.");
        }

        var service = booking.ConsultationService;

        return await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var date = DateOnly.FromDateTime(startUtc.ToOffset(SlotGenerator.DhakaOffset).DateTime);

            // The same function that drew the calendar, with this booking taken
            // out of the diary — so the board cannot put a customer somewhere
            // the booking page would have refused to, and a move of half an
            // hour is not blocked by the appointment being moved.
            var free = await availability.ResolveAsync(service, target, date, date, booking.Id, ct);

            var chosen = free
                .Where(entry => entry.Slots.Contains(startUtc))
                .Cast<ConsultantSlots?>()
                .FirstOrDefault();

            if (chosen is not { } assigned)
            {
                return Fail(
                    ConsultationErrors.SlotNotAvailable,
                    "Nobody is free at that time. Please choose another.");
            }

            var from = booking.Status;
            var at = clock.UtcNow;

            booking.PreviousScheduledAtUtc = booking.ScheduledAtUtc;
            booking.ScheduledAtUtc = startUtc;
            booking.ConsultantId = assigned.ConsultantId;
            booking.Status = BookingStatus.Rescheduled;

            // A reminder already sent was about a time this appointment is no
            // longer at. Clearing the stamps is what makes the customer hear
            // about the new one.
            booking.FirstReminderSentAt = null;
            booking.FinalReminderSentAt = null;

            Record(booking, from, BookingStatus.Rescheduled, note, currentUser.PhoneNumber ?? "Staff", at);

            bookings.Update(booking);

            await QueueAsync(booking, service, "booking.status_changed", at, ct);
            await unitOfWork.SaveChangesAsync(ct);

            ConsultationLog.BookingRescheduled(
                logger,
                booking.BookingNumber,
                booking.PreviousScheduledAtUtc!.Value,
                startUtc,
                assigned.ConsultantName);

            // The board asked for this move, so the board gets the staff view.
            return Ok(booking, maskPhone: false);
        }, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Moving a booking
    // -------------------------------------------------------------------------

    /// <param name="maskPhone">
    /// False when the shop moved it. Staff see the number they have to
    /// telephone; a customer cancelling their own sees theirs masked, as on
    /// every other page they might screenshot. Without this the admin board
    /// would confirm a booking and be handed back a masked number for its
    /// trouble.
    /// </param>
    private async Task<GeneralResponse<BookingDto>> MoveAsync(
        Booking booking,
        BookingStatus to,
        string? note,
        string actor,
        CancellationToken cancellationToken,
        bool maskPhone = true)
    {
        if (!BookingStatusMachine.CanTransition(booking.Status, to))
        {
            return Fail(
                ConsultationErrors.TransitionInvalid,
                $"A booking cannot go from {booking.Status} to {to}.");
        }

        return await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var from = booking.Status;
            var at = clock.UtcNow;

            Record(booking, from, to, note, actor, at);
            booking.Status = to;

            bookings.Update(booking);

            // Told once per move, keyed on the moment of the move. Keyed on the
            // status alone, a booking confirmed, moved and confirmed again
            // would have its second confirmation silently swallowed as a
            // duplicate — and the customer would be told a time that had
            // changed since.
            await QueueAsync(booking, booking.ConsultationService, "booking.status_changed", at, ct);

            await unitOfWork.SaveChangesAsync(ct);

            ConsultationLog.BookingMoved(logger, booking.BookingNumber, from, to, actor);

            return Ok(booking, maskPhone);
        }, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Building
    // -------------------------------------------------------------------------

    private Booking Build(
        CreateBookingDto dto,
        ConsultationService service,
        long consultantId,
        PhoneNumber phone,
        DeliveryAddress? site) =>
        new()
        {
            ConsultationServiceId = service.Id,
            ConsultantId = consultantId,
            CustomerId = currentUser.UserId,
            ContactName = dto.ContactName.Trim(),
            ContactPhone = phone.Value,
            ContactEmail = string.IsNullOrWhiteSpace(dto.ContactEmail) ? null : dto.ContactEmail.Trim(),
            CustomerLanguage = currentUser.Language,
            ScheduledAtUtc = dto.StartUtc,

            // Snapshotted, all of it. A consultation booked in September at
            // 2,000৳ for an hour must still read that way after the shop
            // raises its price or lengthens the appointment.
            DurationMinutes = service.DurationMinutes,
            Currency = service.Fee.Currency,
            Fee = service.Fee,
            AdvanceDue = service.AdvanceDue(),

            SiteAddress = site,
            ProjectBrief = string.IsNullOrWhiteSpace(dto.ProjectBrief) ? null : dto.ProjectBrief.Trim(),
            BudgetRange = string.IsNullOrWhiteSpace(dto.BudgetRange) ? null : dto.BudgetRange.Trim(),
            RoomTypes =
            [
                .. dto.RoomTypes
                    .Where(room => !string.IsNullOrWhiteSpace(room))
                    .Select(room => room.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
            ],

            Status = BookingStatus.Requested
        };

    private void Record(
        Booking booking,
        BookingStatus? from,
        BookingStatus to,
        string? note,
        string? actor = null,
        DateTimeOffset? at = null) =>
        booking.Timeline.Add(new BookingTimelineEntry
        {
            FromStatus = from,
            ToStatus = to,
            ActorUserId = currentUser.UserId,
            ActorName = actor ?? (currentUser.UserId is null ? "Customer" : currentUser.PhoneNumber ?? "Customer"),
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            OccurredAt = at ?? clock.UtcNow
        });

    /// <summary>
    /// Stages the customer's message in the same unit of work as the booking.
    /// </summary>
    /// <remarks>
    /// The outbox is what makes "booking written, customer told" one fact.
    /// Sending inline would either send for a booking that then rolled back, or
    /// lose the message to a crash in between.
    /// </remarks>
    private async Task QueueAsync(
        Booking booking,
        ConsultationService? service,
        string type,
        DateTimeOffset? at,
        CancellationToken ct) =>
        await notifications.EnqueueAsync(
            new NotificationRequest
            {
                Type = type,
                IdempotencyKey = type == "booking.requested"
                    ? $"booking.requested:{booking.BookingNumber}"
                    : $"booking.status:{booking.BookingNumber}:{booking.Status}:{at:O}",
                Payload = JsonSerializer.Serialize(new
                {
                    bookingNumber = booking.BookingNumber,
                    contactName = booking.ContactName,
                    contactPhone = booking.ContactPhone,
                    contactEmail = booking.ContactEmail,
                    language = booking.CustomerLanguage,
                    serviceName = service?.Name.En ?? string.Empty,
                    status = booking.Status.ToString(),

                    // The shop's own time, because the message is read by
                    // somebody in Dhaka and "14:00Z" means nothing to them.
                    scheduledAt = booking.ScheduledAtUtc
                        .ToOffset(SlotGenerator.DhakaOffset)
                        .ToString("dddd d MMMM, h:mm tt", System.Globalization.CultureInfo.InvariantCulture),
                    durationMinutes = booking.DurationMinutes,
                    fee = booking.Fee.Amount,
                    advanceDue = booking.AdvanceDue?.Amount
                })
            },
            ct);

    /// <summary>
    /// Whether this caller may see this booking.
    /// </summary>
    /// <remarks>
    /// A signed-in customer sees their own. A guest sees theirs by quoting the
    /// number and the phone it was booked with — the same two facts an order
    /// is tracked by, and the reason a guessed booking number on its own
    /// discloses nothing.
    /// </remarks>
    private bool MayView(Booking booking, string? contactPhone)
    {
        if (currentUser.UserId is { } userId && booking.CustomerId == userId)
        {
            return true;
        }

        return PhoneNumber.TryParse(contactPhone, out var parsed)
               && parsed is not null
               && string.Equals(parsed.Value, booking.ContactPhone, StringComparison.Ordinal);
    }

    private GeneralResponse<BookingDto> Ok(Booking booking, bool maskPhone = true) =>
        GeneralResponse<BookingDto>.Success(
            ConsultationMapper.ToDto(booking, currentUser.Language, maskPhone), id: booking.Id);

    private static DeliveryAddress ToAddress(DeliveryAddressDto dto) =>
        DeliveryAddress.Create(
            dto.Division,
            dto.District,
            dto.AddressLine,
            dto.Upazila,
            dto.Area,
            dto.Landmark,
            dto.Postcode);

    private static GeneralResponse<BookingDto> Fail(string code, string message) =>
        GeneralResponse<BookingDto>.Fail(code, message);
}

/// <summary>
/// Structured logging for consultations.
/// </summary>
/// <remarks>Event ids 2200–2202.</remarks>
internal static partial class ConsultationLog
{
    [LoggerMessage(
        EventId = 2200,
        Level = LogLevel.Information,
        Message = "Booking {BookingNumber} requested for {ScheduledAt} with {Consultant}.")]
    public static partial void BookingRequested(
        ILogger logger, string bookingNumber, DateTimeOffset scheduledAt, string consultant);

    [LoggerMessage(
        EventId = 2201,
        Level = LogLevel.Information,
        Message = "Booking {BookingNumber} moved from {From} to {To} by {Actor}.")]
    public static partial void BookingMoved(
        ILogger logger, string bookingNumber, BookingStatus from, BookingStatus to, string actor);

    [LoggerMessage(
        EventId = 2203,
        Level = LogLevel.Information,
        Message = "Booking {BookingNumber} moved from {From} to {To}, with {Consultant}.")]
    public static partial void BookingRescheduled(
        ILogger logger,
        string bookingNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        string consultant);

    [LoggerMessage(
        EventId = 2202,
        Level = LogLevel.Information,
        Message = "Consultation schedule for {Consultant} replaced: {Rules} rule(s), {Exceptions} exception(s).")]
    public static partial void ScheduleSaved(
        ILogger logger, string consultant, int rules, int exceptions);
}
