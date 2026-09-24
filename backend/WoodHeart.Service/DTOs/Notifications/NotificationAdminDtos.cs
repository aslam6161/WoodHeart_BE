using System.ComponentModel.DataAnnotations;
using WoodHeart.Domain.Enums.Common;
using WoodHeart.Domain.Enums.Notifications;

namespace WoodHeart.Service.DTOs.Notifications;

/// <summary>
/// Everything the notifications screen needs to open.
/// </summary>
/// <remarks>
/// The three flags are properties of the installation rather than of any one
/// template, which is why they are here and not repeated on each row. They
/// matter because a shop with no SMS key configured has a queue that reports
/// every message delivered while nothing has left the building — the sender
/// writes to the log instead, which is right on a developer's machine and
/// disastrous to discover in production from a customer.
/// </remarks>
public class NotificationTemplatesDto
{
    public IReadOnlyList<NotificationTemplateDto> Templates { get; init; } = [];

    public bool SmsGatewayConfigured { get; init; }

    public bool EmailConfigured { get; init; }

    /// <summary>
    /// Whether <c>store.phone</c> has been set.
    /// </summary>
    /// <remarks>
    /// <b>Every message ends with it.</b> Unset, the confirmation SMS stops
    /// mid-sentence and the email says "Any questions, please call ." — to
    /// every customer, on every order, with nothing anywhere complaining. It
    /// is reported here rather than only on the settings screen because this
    /// is the screen where the consequence is visible: the previews below show
    /// the gap.
    /// </remarks>
    public bool ShopPhoneConfigured { get; init; }
}

/// <summary>One kind of message, and whether the shop is sending it.</summary>
public class NotificationTemplateDto
{
    public string Code { get; init; } = null!;

    /// <summary>"Order confirmation", not "order.placed".</summary>
    public string Name { get; init; } = null!;

    public string WhenItFires { get; init; } = null!;

    public NotificationAudience Audience { get; init; }

    public bool SmsEnabled { get; init; }

    public bool EmailEnabled { get; init; }

    /// <summary>
    /// Billed parts for one English message.
    /// </summary>
    /// <remarks>
    /// On the list rather than only the preview, because the list is where a
    /// shop decides what it is paying for. One part is 160 characters; the
    /// Bangla form of the same message is billed every 70, which is why the two
    /// counts sit side by side.
    /// </remarks>
    public int SmsParts { get; init; }

    /// <summary>Billed parts in Bangla, or null where the message has no Bangla form.</summary>
    public int? BanglaSmsParts { get; init; }
}

/// <summary>One template, with the messages it would actually produce.</summary>
public class NotificationTemplateDetailDto : NotificationTemplateDto
{
    /// <summary>One entry per language the message has a form in.</summary>
    public IReadOnlyList<NotificationPreviewDto> Previews { get; init; } = [];

    public bool SmsGatewayConfigured { get; init; }

    public bool EmailConfigured { get; init; }

    /// <inheritdoc cref="NotificationTemplatesDto.ShopPhoneConfigured" />
    public bool ShopPhoneConfigured { get; init; }
}

/// <summary>What one template says, rendered from a sample.</summary>
public class NotificationPreviewDto
{
    /// <summary><c>en</c> or <c>bn</c>.</summary>
    public string Language { get; init; } = null!;

    public string? SmsText { get; init; }

    public int SmsParts { get; init; }

    /// <summary>True once a single non-ASCII character drops the part size from 160 to 70.</summary>
    public bool IsUnicode { get; init; }

    public string? EmailSubject { get; init; }

    public string? EmailHtml { get; init; }
}

/// <summary>
/// The only two things about a template that are the shop's to decide.
/// </summary>
/// <remarks>
/// Not the wording. An SMS is billed per part and every message in
/// <c>NotificationTemplates</c> is written to fit the fewest parts that still
/// say the thing, in two languages; a text box here would let an afternoon's
/// editing triple the gateway invoice with nothing to say it had.
/// </remarks>
public class UpdateNotificationTemplateDto
{
    public bool SmsEnabled { get; init; }

    public bool EmailEnabled { get; init; }
}

/// <summary>One queued, sent or failed message.</summary>
public class NotificationMessageDto
{
    public long Id { get; init; }

    public string Type { get; init; } = null!;

    /// <summary>The template's readable name, where the type is one we know.</summary>
    public string? TemplateName { get; init; }

    public OutboxStatus Status { get; init; }

    /// <summary>The order, booking or quotation number this message is about.</summary>
    public string? Reference { get; init; }

    /// <summary>Masked, because a list of failures should name nobody in full.</summary>
    public string? Recipient { get; init; }

    public int AttemptCount { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Set on a reminder held until the day before the appointment.</summary>
    public DateTimeOffset? NotBefore { get; init; }

    public DateTimeOffset? NextAttemptAt { get; init; }

    public DateTimeOffset? ProcessedAt { get; init; }

    /// <summary>Why it failed, or why it was skipped. The single most useful column here.</summary>
    public string? LastError { get; init; }

    /// <summary>Ties this back to the request that caused it, in the logs.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Whether the panel will put this one back in the queue.
    /// </summary>
    /// <remarks>
    /// False for a message already queued or one a worker is holding — the
    /// first would do nothing but look as though it had, the second would race
    /// the worker into sending the same SMS twice.
    /// </remarks>
    public bool CanResend { get; init; }
}

public class NotificationMessageQueryDto
{
    /// <summary>An order, booking or quotation number.</summary>
    [StringLength(60)]
    public string? Term { get; init; }

    public OutboxStatus? Status { get; init; }

    [StringLength(128)]
    public string? Type { get; init; }

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}
