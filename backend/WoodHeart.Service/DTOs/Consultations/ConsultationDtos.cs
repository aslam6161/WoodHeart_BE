using System.ComponentModel.DataAnnotations;
using WoodHeart.Domain.Enums.Consultations;
using WoodHeart.Service.DTOs.Ordering;

namespace WoodHeart.Service.DTOs.Consultations;

// -----------------------------------------------------------------------------
// What the shop offers
// -----------------------------------------------------------------------------

/// <summary>One thing that can be booked, as the booking page renders it.</summary>
public class ConsultationServiceDto
{
    public long Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Slug { get; init; } = string.Empty;

    public string? Description { get; init; }

    public ConsultationMode Mode { get; init; }

    public int DurationMinutes { get; init; }

    /// <summary>Zero is a free consultation, and that is a real option.</summary>
    public decimal Fee { get; init; }

    public bool RequiresAdvance { get; init; }

    /// <summary>What should be collected up front. Null when nothing should.</summary>
    public decimal? AdvanceDue { get; init; }

    public bool IsActive { get; init; }

    public int SortOrder { get; init; }

    /// <summary>Who offers it. Empty means nobody, and nothing can be booked.</summary>
    public IReadOnlyList<ConsultantSummaryDto> Consultants { get; init; } = [];
}

/// <summary>A consultant, just enough to pick one.</summary>
public class ConsultantSummaryDto
{
    public long Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? PhotoPath { get; init; }
}

/// <summary>A consultant in full, for the "who am I booking" panel.</summary>
public class ConsultantDto : ConsultantSummaryDto
{
    public string? Bio { get; init; }

    public IReadOnlyList<string> Specialities { get; init; } = [];

    public IReadOnlyList<long> ServiceIds { get; init; } = [];

    public bool IsActive { get; init; }

    public int SortOrder { get; init; }
}

// -----------------------------------------------------------------------------
// When it can happen
// -----------------------------------------------------------------------------

/// <summary>What the calendar is asking for.</summary>
public class AvailabilityQueryDto
{
    [Range(1, long.MaxValue)]
    public long ServiceId { get; init; }

    /// <summary>Null means "anybody", and the API picks whoever is free.</summary>
    public long? ConsultantId { get; init; }

    /// <summary>The first Dhaka date to look at. Defaults to today.</summary>
    public DateOnly? From { get; init; }

    /// <summary>The last Dhaka date, inclusive. Defaults to a fortnight out.</summary>
    public DateOnly? To { get; init; }
}

/// <summary>The times on offer, grouped by the day a customer sees them under.</summary>
public class AvailabilityDto
{
    public long ServiceId { get; init; }

    public int DurationMinutes { get; init; }

    public IReadOnlyList<AvailabilityDayDto> Days { get; init; } = [];
}

/// <summary>One Dhaka date and what is free on it.</summary>
public class AvailabilityDayDto
{
    public DateOnly Date { get; init; }

    public IReadOnlyList<AvailabilitySlotDto> Slots { get; init; } = [];
}

/// <summary>
/// One startable time.
/// </summary>
/// <remarks>
/// The consultant is named even when the customer asked for "anybody": the
/// slot belongs to somebody's afternoon, and saying whose is both more honest
/// and what the booking will actually record.
/// </remarks>
public class AvailabilitySlotDto
{
    public DateTimeOffset StartUtc { get; init; }

    public long ConsultantId { get; init; }

    public string ConsultantName { get; init; } = string.Empty;
}

// -----------------------------------------------------------------------------
// Booking one
// -----------------------------------------------------------------------------

/// <summary>What a customer sends to book an appointment.</summary>
public class CreateBookingDto
{
    [Range(1, long.MaxValue)]
    public long ServiceId { get; init; }

    /// <summary>Null means "anybody", and the API assigns whoever is free.</summary>
    public long? ConsultantId { get; init; }

    /// <summary>The slot, exactly as the availability endpoint gave it.</summary>
    public DateTimeOffset StartUtc { get; init; }

    [Required]
    [StringLength(120, MinimumLength = 2)]
    public string ContactName { get; init; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string ContactPhone { get; init; } = string.Empty;

    [EmailAddress]
    [StringLength(256)]
    public string? ContactEmail { get; init; }

    /// <summary>Required for a site visit. Ignored for the other modes.</summary>
    public DeliveryAddressDto? SiteAddress { get; init; }

    [StringLength(2000)]
    public string? ProjectBrief { get; init; }

    [StringLength(60)]
    public string? BudgetRange { get; init; }

    public IReadOnlyList<string> RoomTypes { get; init; } = [];
}

/// <summary>A booking as the customer sees it.</summary>
public class BookingDto
{
    public long Id { get; init; }

    public string BookingNumber { get; init; } = string.Empty;

    public string ServiceName { get; init; } = string.Empty;

    public ConsultationMode Mode { get; init; }

    public string? ConsultantName { get; init; }

    public DateTimeOffset ScheduledAtUtc { get; init; }

    public int DurationMinutes { get; init; }

    public BookingStatus Status { get; init; }

    public string ContactName { get; init; } = string.Empty;

    /// <summary>Masked for the customer's own view — <c>+88017*****678</c>.</summary>
    public string ContactPhone { get; init; } = string.Empty;

    public string? ContactEmail { get; init; }

    public DeliveryAddressDto? SiteAddress { get; init; }

    public string? ProjectBrief { get; init; }

    public string? BudgetRange { get; init; }

    public IReadOnlyList<string> RoomTypes { get; init; } = [];

    public decimal Fee { get; init; }

    public decimal? AdvanceDue { get; init; }

    public IReadOnlyList<BookingTimelineEntryDto> Timeline { get; init; } = [];

    /// <summary>Whether the customer may still call it off themselves.</summary>
    public bool CanCancel { get; init; }
}

public class BookingTimelineEntryDto
{
    public BookingStatus? FromStatus { get; init; }

    public BookingStatus ToStatus { get; init; }

    public string ActorName { get; init; } = string.Empty;

    public string? Note { get; init; }

    public DateTimeOffset OccurredAt { get; init; }
}

/// <summary>Moving a booking along, from the admin board or by the customer.</summary>
public class ChangeBookingStatusDto
{
    public BookingStatus Status { get; init; }

    [StringLength(500)]
    public string? Note { get; init; }
}

// -----------------------------------------------------------------------------
// Writing the schedule
// -----------------------------------------------------------------------------

/// <summary>Creating or replacing a consultation service.</summary>
public class SaveConsultationServiceDto
{
    [Required]
    [StringLength(160, MinimumLength = 2)]
    public string NameEn { get; init; } = string.Empty;

    [StringLength(160)]
    public string? NameBn { get; init; }

    [StringLength(160)]
    public string? Slug { get; init; }

    [StringLength(2000)]
    public string? DescriptionEn { get; init; }

    [StringLength(2000)]
    public string? DescriptionBn { get; init; }

    public ConsultationMode Mode { get; init; } = ConsultationMode.InStudio;

    [Range(5, 600)]
    public int DurationMinutes { get; init; } = 60;

    [Range(0, 9_999_999)]
    public decimal Fee { get; init; }

    public bool RequiresAdvance { get; init; }

    [Range(0, 9_999_999)]
    public decimal? AdvanceAmount { get; init; }

    [Range(0, 480)]
    public int BufferBeforeMinutes { get; init; }

    [Range(0, 480)]
    public int BufferAfterMinutes { get; init; }

    public bool IsActive { get; init; } = true;

    [Range(0, 1000)]
    public int SortOrder { get; init; }
}

/// <summary>Creating or replacing a consultant, and what they offer.</summary>
public class SaveConsultantDto
{
    [Required]
    [StringLength(160, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;

    [StringLength(512)]
    public string? PhotoPath { get; init; }

    [StringLength(2000)]
    public string? BioEn { get; init; }

    [StringLength(2000)]
    public string? BioBn { get; init; }

    public IReadOnlyList<string> Specialities { get; init; } = [];

    /// <summary>The whole set. Sent in full, like the discount's targets.</summary>
    public IReadOnlyList<long> ServiceIds { get; init; } = [];

    public bool IsActive { get; init; } = true;

    [Range(0, 1000)]
    public int SortOrder { get; init; }
}

/// <summary>
/// A consultant's whole diary, replaced in one go.
/// </summary>
/// <remarks>
/// All-or-nothing, like the settings screen. A week is one decision — the
/// Friday off only makes sense beside the Saturday morning — and a partial
/// save is how a consultant ends up available on a day nobody meant.
/// </remarks>
public class SaveScheduleDto
{
    public IReadOnlyList<AvailabilityRuleDto> Rules { get; init; } = [];

    public IReadOnlyList<AvailabilityExceptionDto> Exceptions { get; init; } = [];
}

public class AvailabilityRuleDto
{
    public DayOfWeek DayOfWeek { get; init; }

    public TimeOnly StartTime { get; init; }

    public TimeOnly EndTime { get; init; }

    [Range(5, 480)]
    public int SlotMinutes { get; init; } = 30;
}

public class AvailabilityExceptionDto
{
    public DateOnly Date { get; init; }

    public bool IsClosed { get; init; } = true;

    public TimeOnly? StartTime { get; init; }

    public TimeOnly? EndTime { get; init; }

    [StringLength(300)]
    public string? Note { get; init; }
}

/// <summary>A consultant with their diary, for the admin edit screen.</summary>
public class ConsultantScheduleDto : ConsultantDto
{
    public IReadOnlyList<AvailabilityRuleDto> Rules { get; init; } = [];

    public IReadOnlyList<AvailabilityExceptionDto> Exceptions { get; init; } = [];
}

/// <summary>Limits shared between the attributes and the services.</summary>
public static class BookingRules
{
    /// <summary>How far ahead the calendar looks when nobody says.</summary>
    public const int DefaultHorizonDays = 14;

    public const int DefaultPageSize = 20;

    public const int MaxPageSize = 100;
}
