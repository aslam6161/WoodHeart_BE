using WoodHeart.Domain.Common;

namespace WoodHeart.Application.Common.Abstractions;

/// <summary>Sends one SMS. Implemented per gateway (Alpha SMS, BulkSMSBD, SSL Wireless).</summary>
/// <remarks>
/// SMS is the primary channel in Bangladesh and it costs real money per message
/// — roughly 0.25–0.45 BDT. Implementations must report cost and a provider
/// message id so spend is auditable and a "did it actually send?" question has
/// an answer.
/// <para>
/// Bangla text is sent as Unicode, which reduces an SMS part from 160 to 70
/// characters. A two-line Bangla message is therefore three billed parts, not
/// one — implementations must return the real part count.
/// </para>
/// </remarks>
public interface ISmsSender
{
    Task<Result<SmsDeliveryReceipt>> SendAsync(
        PhoneNumber recipient,
        string message,
        CancellationToken cancellationToken = default);
}

public sealed record SmsDeliveryReceipt(string ProviderMessageId, int Parts, decimal? Cost);

/// <summary>Sends one email.</summary>
public interface IEmailSender
{
    Task<Result<string>> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed record EmailMessage
{
    public required string To { get; init; }

    public required string Subject { get; init; }

    public required string HtmlBody { get; init; }

    public string? PlainTextBody { get; init; }

    public IReadOnlyList<EmailAttachment> Attachments { get; init; } = [];
}

public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);

/// <summary>Renders a notification template with a payload. Scriban-backed.</summary>
public interface ITemplateRenderer
{
    Task<Result<string>> RenderAsync(
        string template,
        object model,
        CancellationToken cancellationToken = default);
}
