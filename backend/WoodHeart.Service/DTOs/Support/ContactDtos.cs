using System.ComponentModel.DataAnnotations;
using WoodHeart.Domain.Enums.Support;

namespace WoodHeart.Service.DTOs.Support;

/// <summary>
/// How to reach the shop, as the shop has filled it in.
/// </summary>
/// <remarks>
/// <b>Every field is nullable on purpose.</b> A shop that has not entered its
/// address yet should show a contact page without an address line, not one
/// reading "Address: —". The storefront leaves out whatever is null, so the
/// page is always honest about what it knows.
/// </remarks>
public class ContactDetailsDto
{
    public string? ShopName { get; init; }

    /// <summary>National form, for a human to read: <c>01712345678</c>.</summary>
    public string? Phone { get; init; }

    /// <summary>E.164, for <c>tel:</c> and WhatsApp links: <c>+8801712345678</c>.</summary>
    public string? PhoneE164 { get; init; }

    public string? Email { get; init; }

    public string? Address { get; init; }

    /// <summary>Opening hours and how quickly somebody replies, in the shop's own words.</summary>
    public string? Hours { get; init; }
}

/// <summary>A message on its way to the shop.</summary>
public class SubmitContactMessageDto
{
    [Required]
    [StringLength(120, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;

    /// <summary>Either this or <see cref="Email"/>. The service insists on one.</summary>
    [StringLength(20)]
    public string? Phone { get; init; }

    /// <summary>Either this or <see cref="Phone"/>.</summary>
    [EmailAddress]
    [StringLength(256)]
    public string? Email { get; init; }

    public ContactTopic Topic { get; init; } = ContactTopic.General;

    /// <summary>An order, quotation or booking number, if they have one to hand.</summary>
    [StringLength(40)]
    public string? Reference { get; init; }

    /// <summary>
    /// What they want to say.
    /// </summary>
    /// <remarks>
    /// A floor of ten characters, because "hi" is not an enquiry and answering
    /// it costs somebody a telephone call to find out what it was about.
    /// </remarks>
    [Required]
    [StringLength(2000, MinimumLength = 10)]
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// What the customer is told after sending.
/// </summary>
/// <remarks>
/// Carries no id. A reference number would be a promise of a tracking page that
/// does not exist, and the honest answer — somebody will be in touch on the
/// contact you left — is the one the page gives.
/// </remarks>
public class ContactReceiptDto
{
    /// <summary>Where the reply will come: the phone or the email they left.</summary>
    public string ReplyTo { get; init; } = string.Empty;

    /// <summary>Opening hours, so "when will I hear back" is answered in place.</summary>
    public string? Hours { get; init; }
}

/// <summary>One line in the shop's inbox.</summary>
public class ContactMessageListItemDto
{
    public long Id { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>Masked: <c>017****5678</c>. The full number is on the message itself.</summary>
    public string? Phone { get; init; }

    public string? Email { get; init; }

    public ContactTopic Topic { get; init; }

    public ContactMessageStatus Status { get; init; }

    /// <summary>The opening of the message, so the list can be triaged without opening each one.</summary>
    public string Preview { get; init; } = string.Empty;

    public string? Reference { get; init; }

    public DateTimeOffset ReceivedAt { get; init; }
}

/// <summary>One message, opened.</summary>
public class ContactMessageDto
{
    public long Id { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The number in full, because this is the screen somebody rings from.
    /// </summary>
    /// <remarks>
    /// Masked in the list and whole here: the list is read over somebody's
    /// shoulder in an office, and a number nobody can dial is not a contact.
    /// </remarks>
    public string? Phone { get; init; }

    public string? Email { get; init; }

    public ContactTopic Topic { get; init; }

    public ContactMessageStatus Status { get; init; }

    public string Message { get; init; } = string.Empty;

    public string? Reference { get; init; }

    public string? StaffNote { get; init; }

    public DateTimeOffset ReceivedAt { get; init; }

    public DateTimeOffset? AnsweredAt { get; init; }
}

/// <summary>Filters for the inbox.</summary>
public class ContactMessageQueryDto
{
    public ContactMessageStatus? Status { get; init; }

    public ContactTopic? Topic { get; init; }

    [StringLength(60)]
    public string? Term { get; init; }

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}

/// <summary>Moving a message along, and what staff want to remember about it.</summary>
public class UpdateContactMessageDto
{
    [Required]
    public ContactMessageStatus Status { get; init; }

    [StringLength(1000)]
    public string? StaffNote { get; init; }
}
