using Hangfire;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;

namespace WoodHeart.Service.Interfaces.Notifications;

/// <summary>
/// Sends one SMS. Implemented per gateway — Alpha SMS, BulkSMSBD, SSL Wireless.
/// </summary>
/// <remarks>
/// <para>
/// SMS is the primary channel in this market and it costs real money per
/// message, roughly 0.25–0.45 BDT. Implementations must report the part count
/// and the provider's message id, so spend is auditable and "did it actually
/// send?" has an answer.
/// </para>
/// <para>
/// Bangla is sent as Unicode, which cuts an SMS part from 160 characters to 70.
/// A two-line Bangla order confirmation is therefore three billed parts, not
/// one. Implementations must return the real count rather than assuming one.
/// </para>
/// </remarks>
public interface ISmsSender
{
    Task<GeneralResponse<SmsDeliveryReceipt>> SendAsync(
        PhoneNumber recipient, string message, CancellationToken cancellationToken = default);
}

public record SmsDeliveryReceipt(string ProviderMessageId, int Parts, decimal? Cost);

/// <summary>Sends one email.</summary>
public interface IEmailSender
{
    Task<GeneralResponse<string>> SendAsync(
        EmailMessage message, CancellationToken cancellationToken = default);
}

public record EmailMessage
{
    public required string To { get; init; }

    public required string Subject { get; init; }

    public required string HtmlBody { get; init; }

    public string? PlainTextBody { get; init; }

    public IReadOnlyList<EmailAttachment> Attachments { get; init; } = [];
}

public record EmailAttachment(string FileName, string ContentType, byte[] Content);

/// <summary>
/// Stages a notification in the outbox.
/// </summary>
/// <remarks>
/// Note what this does <b>not</b> do: it does not save. The row joins whatever
/// unit of work the calling service already has open, which is the entire point
/// of the outbox — the SMS and the order it announces commit together or not at
/// all. See <see cref="IUnitOfWork"/>.
/// </remarks>
public interface INotificationQueue
{
    Task EnqueueAsync(NotificationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Drains the outbox. The only thing in the application that talks to a
/// gateway.
/// </summary>
/// <remarks>
/// An interface so the recurring job can be scheduled against it by name, and
/// so the whole delivery path can be exercised without Hangfire in the test.
/// </remarks>
public interface IOutboxDispatcher
{
    /// <summary>One pass over the queue.</summary>
    /// <remarks>
    /// <para>
    /// The Hangfire attributes live here rather than on the implementation, and
    /// that is not a style choice: the recurring job is registered against
    /// <c>IOutboxDispatcher</c>, so this is the method Hangfire reflects over.
    /// Attributes on <c>OutboxDispatcher</c> are silently ignored — which is
    /// how a job meant not to retry ends up retrying ten times.
    /// </para>
    /// <para>
    /// <b>No automatic retry.</b> The outbox does its own, with a growing gap
    /// and a cap, recorded on the row where a person can see it. Hangfire
    /// retrying on top of that would re-run the whole batch and re-send every
    /// message that had already gone.
    /// </para>
    /// <para>
    /// <b>No concurrent execution.</b> Two overlapping passes are safe —
    /// <c>FOR UPDATE SKIP LOCKED</c> sees to that — but never useful, and a
    /// backed-up queue would otherwise stack passes until the connection pool
    /// ran out.
    /// </para>
    /// </remarks>
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    [AutomaticRetry(Attempts = 0)]
    Task RunAsync(CancellationToken cancellationToken = default);
}

public record NotificationRequest
{
    /// <summary>Template code, e.g. <c>order.placed</c> or <c>consultation.reminder</c>.</summary>
    public required string Type { get; init; }

    /// <summary>JSON the delivery worker renders the template with.</summary>
    public required string Payload { get; init; }

    /// <summary>Deduplication key. Two enqueues with the same key deliver once.</summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>Hold until this instant — how a booking reminder at T−24h is scheduled.</summary>
    public DateTimeOffset? NotBefore { get; init; }
}
