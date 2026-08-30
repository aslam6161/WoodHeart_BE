using WoodHeart.Domain.Enums.Common;

namespace WoodHeart.Domain.Entity.Common;

/// <summary>
/// A side effect waiting to be delivered — an SMS, an email, later an
/// integration event.
/// </summary>
/// <remarks>
/// <para>
/// The row is inserted in the <em>same transaction</em> as the business change
/// that caused it. That one fact removes the two worst failure modes of naive
/// notification code:
/// </para>
/// <list type="number">
///   <item>An order is confirmed but the customer is never told, because the
///         SMS call threw and nobody retried it.</item>
///   <item>An SMS gateway outage rolls back a perfectly good order, because the
///         send sat inside the business transaction.</item>
/// </list>
/// <para>
/// A Hangfire worker polls due rows, delivers them, and records the outcome.
/// Delivery is at-least-once, so consumers must tolerate a repeat — hence
/// <see cref="IdempotencyKey"/>, which is uniquely indexed.
/// </para>
/// <para>
/// This matters more here than it would elsewhere: SMS in Bangladesh is billed
/// per message part, and a Bangla message costs a part every 70 characters. A
/// retry storm is a line item on an invoice, not just noise in a log.
/// </para>
/// </remarks>
public class OutboxMessage : BaseEntity
{
    /// <summary>Notification template code or event name, e.g. <c>order.placed</c>.</summary>
    public string Type { get; set; } = null!;

    /// <summary>JSON payload the delivery worker renders the template with.</summary>
    public string Payload { get; set; } = null!;

    /// <summary>Deduplication key. Unique where present.</summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>Hold delivery until this instant — used for booking reminders at T−24h.</summary>
    public DateTimeOffset? NotBefore { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;

    public int AttemptCount { get; set; }

    public DateTimeOffset? NextAttemptAt { get; set; }

    public string? LastError { get; set; }

    /// <summary>Ties the eventual SMS back to the HTTP request that triggered it.</summary>
    public string? CorrelationId { get; set; }
}
