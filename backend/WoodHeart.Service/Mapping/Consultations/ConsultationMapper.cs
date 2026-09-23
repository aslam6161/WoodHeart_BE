using WoodHeart.Domain.Consultations;
using WoodHeart.Domain.Entity.Consultations;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Service.DTOs.Consultations;
using WoodHeart.Service.Mapping.Ordering;

namespace WoodHeart.Service.Mapping.Consultations;

/// <summary>
/// Consultation entities to the shapes the screens render.
/// </summary>
/// <remarks>
/// Hand-written like the rest. The decisions worth seeing: a booking's phone
/// number is masked for the customer's own view, exactly as an order's is —
/// a confirmation is a thing people screenshot — and the advance due is
/// computed from the service rather than stored twice.
/// </remarks>
public static class ConsultationMapper
{
    public static ConsultationServiceDto ToDto(ConsultationService service, string? language) =>
        new()
        {
            Id = service.Id,
            Name = service.Name.For(language),
            Slug = service.Slug.Value,
            Description = service.Description?.For(language),
            Mode = service.Mode,
            DurationMinutes = service.DurationMinutes,
            Fee = service.Fee.Amount,
            RequiresAdvance = service.RequiresAdvance,
            AdvanceDue = service.AdvanceDue()?.Amount,
            IsActive = service.IsActive,
            SortOrder = service.SortOrder,
            Consultants =
            [
                .. service.Consultants
                    .Where(link => link.Consultant is not null)
                    .Select(link => new ConsultantSummaryDto
                    {
                        Id = link.Consultant.Id,
                        Name = link.Consultant.Name,
                        PhotoPath = link.Consultant.PhotoPath
                    })
            ]
        };

    public static ConsultantDto ToDto(Consultant consultant, string? language) =>
        new()
        {
            Id = consultant.Id,
            Name = consultant.Name,
            PhotoPath = consultant.PhotoPath,
            Bio = consultant.Bio?.For(language),
            Specialities = [.. consultant.Specialities],
            ServiceIds = [.. consultant.Services.Select(link => link.ConsultationServiceId)],
            IsActive = consultant.IsActive,
            SortOrder = consultant.SortOrder
        };

    public static ConsultantScheduleDto ToScheduleDto(Consultant consultant, string? language) =>
        new()
        {
            Id = consultant.Id,
            Name = consultant.Name,
            PhotoPath = consultant.PhotoPath,
            Bio = consultant.Bio?.For(language),
            Specialities = [.. consultant.Specialities],
            ServiceIds = [.. consultant.Services.Select(link => link.ConsultationServiceId)],
            IsActive = consultant.IsActive,
            SortOrder = consultant.SortOrder,
            Rules =
            [
                .. consultant.AvailabilityRules
                    .OrderBy(rule => rule.DayOfWeek)
                    .ThenBy(rule => rule.StartTime)
                    .Select(rule => new AvailabilityRuleDto
                    {
                        DayOfWeek = rule.DayOfWeek,
                        StartTime = rule.StartTime,
                        EndTime = rule.EndTime,
                        SlotMinutes = rule.SlotMinutes
                    })
            ],
            Exceptions =
            [
                .. consultant.AvailabilityExceptions
                    .OrderBy(entry => entry.Date)
                    .Select(entry => new AvailabilityExceptionDto
                    {
                        Date = entry.Date,
                        IsClosed = entry.IsClosed,
                        StartTime = entry.StartTime,
                        EndTime = entry.EndTime,
                        Note = entry.Note
                    })
            ]
        };

    /// <summary>
    /// A booking for the person it belongs to.
    /// </summary>
    /// <param name="maskPhone">
    /// True for the customer's own view, false for staff. The customer knows
    /// their own number and a confirmation page gets screenshotted; the shop
    /// needs the number to telephone them.
    /// </param>
    public static BookingDto ToDto(Booking booking, string? language, bool maskPhone = true) =>
        new()
        {
            Id = booking.Id,
            BookingNumber = booking.BookingNumber,
            ServiceName = booking.ConsultationService?.Name.For(language) ?? string.Empty,
            Mode = booking.ConsultationService?.Mode ?? default,
            ConsultantName = booking.Consultant?.Name,
            ScheduledAtUtc = booking.ScheduledAtUtc,
            DurationMinutes = booking.DurationMinutes,
            Status = booking.Status,
            ContactName = booking.ContactName,
            ContactPhone = maskPhone ? Mask(booking.ContactPhone) : booking.ContactPhone,
            ContactEmail = booking.ContactEmail,
            SiteAddress = booking.SiteAddress is null
                ? null
                : OrderMapper.ToAddressDto(booking.SiteAddress),
            ProjectBrief = booking.ProjectBrief,
            BudgetRange = booking.BudgetRange,
            RoomTypes = [.. booking.RoomTypes],
            Fee = booking.Fee.Amount,
            AdvanceDue = booking.AdvanceDue?.Amount,
            Timeline =
            [
                .. booking.Timeline.Select(entry => new BookingTimelineEntryDto
                {
                    FromStatus = entry.FromStatus,
                    ToStatus = entry.ToStatus,
                    ActorName = entry.ActorName,
                    Note = entry.Note,
                    OccurredAt = entry.OccurredAt
                })
            ],
            CanCancel = BookingStatusMachine.IsCustomerCancellable(booking.Status)
        };

    /// <summary>
    /// A booking as one row of the shop's diary.
    /// </summary>
    /// <remarks>
    /// The phone number is not masked. The board is behind a staff policy and
    /// the commonest thing done from it is telephoning the customer; a masked
    /// number would mean opening every row to ring anybody.
    /// </remarks>
    public static BookingListItemDto ToListItem(Booking booking, string? language) =>
        new()
        {
            Id = booking.Id,
            BookingNumber = booking.BookingNumber,
            ServiceName = booking.ConsultationService?.Name.For(language) ?? string.Empty,
            Mode = booking.ConsultationService?.Mode ?? default,
            ConsultantName = booking.Consultant?.Name,
            ScheduledAtUtc = booking.ScheduledAtUtc,
            DurationMinutes = booking.DurationMinutes,
            Status = booking.Status,
            ContactName = booking.ContactName,
            ContactPhone = booking.ContactPhone,
            SiteLocation = Where(booking.SiteAddress),
            Fee = booking.Fee.Amount,
            AdvanceDue = booking.AdvanceDue?.Amount,
            RequestedAt = booking.CreatedAt
        };

    /// <summary>
    /// "Dhanmondi, Dhaka" — the two lines of an address that decide whether a
    /// site visit fits in the same afternoon as the one before it.
    /// </summary>
    private static string? Where(DeliveryAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        var area = string.IsNullOrWhiteSpace(address.Area) ? address.Upazila : address.Area;

        return string.IsNullOrWhiteSpace(area) ? address.District : $"{area}, {address.District}";
    }

    private static string Mask(string phone) =>
        PhoneNumber.TryParse(phone, out var parsed) && parsed is not null ? parsed.Masked : phone;
}
