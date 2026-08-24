namespace WoodHeart.Infrastructure.Persistence.Outbox;

/// <summary>
/// A side effect waiting to be delivered — an SMS, an email, later an
/// integration event.
/// </summary>
/// <remarks>
/// <para>
/// The row is inserted in the <em>same transaction</em> as the business change
/// that caused it. That single fact removes the two worst failure modes of
/// naive notification code:
/// </para>
/// <list type="number">
///   <item>An order is confirmed but the customer is never told, because the
///         SMS call threw and nobody retried it.</item>
///   <item>An SMS gateway outage rolls back a perfectly good order, because the
///         send was inside the business transaction.</item>
/// </list>
/// <para>
/// A Hangfire worker polls unprocessed rows, delivers them, and records the
/// outcome. Delivery is at-least-once, so consumers must tolerate a repeat —
/// hence <see cref="IdempotencyKey"/>.
/// </para>
/// </remarks>
public sealed class OutboxMessage
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Notification template code or integration event name, e.g. <c>order.placed</c>.</summary>
    public required string Type { get; set; }

    /// <summary>JSON payload the delivery worker renders the template with.</summary>
    public required string Payload { get; set; }

    public string? IdempotencyKey { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Hold delivery until this instant — used for booking reminders at T−24h.</summary>
    public DateTimeOffset? NotBeforeUtc { get; set; }

    public DateTimeOffset? ProcessedAtUtc { get; set; }

    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;

    public int AttemptCount { get; set; }

    public DateTimeOffset? NextAttemptAtUtc { get; set; }

    public string? LastError { get; set; }

    /// <summary>Ties the eventual SMS back to the HTTP request that triggered it.</summary>
    public string? CorrelationId { get; set; }
}

public enum OutboxStatus
{
    Pending = 0,
    Processing = 1,
    Processed = 2,

    /// <summary>Retries exhausted. Needs a human — surfaced on the admin dashboard.</summary>
    Failed = 3,

    /// <summary>Deliberately skipped, e.g. its notification template was disabled.</summary>
    Suppressed = 4
}
