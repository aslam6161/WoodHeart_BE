using WoodHeart.Domain.Common;

namespace WoodHeart.Application.Common.Abstractions;

/// <summary>
/// Publishes domain events collected during a save, after the transaction commits.
/// </summary>
/// <remarks>
/// <b>After</b> is the important word. If events dispatched inside the
/// transaction, a handler that sends an SMS could roll back a confirmed order
/// when the gateway times out — the tail wagging the dog. Dispatching
/// post-commit means the business fact is durable first, and side effects are
/// made reliable separately by the outbox.
/// </remarks>
public interface IDomainEventDispatcher
{
    Task DispatchAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}

/// <summary>
/// Writes a message to the transactional outbox.
/// </summary>
/// <remarks>
/// <para>
/// The outbox row is written in the <em>same</em> transaction as the business
/// change, so "order confirmed" and "confirmation SMS queued" either both
/// happen or neither does. A background worker then delivers it with retries.
/// </para>
/// <para>
/// This is what removes the two worst failure modes of naive notification code:
/// an order that silently never notifies the customer, and an SMS outage that
/// takes the checkout down with it.
/// </para>
/// </remarks>
public interface IOutbox
{
    void Enqueue(OutboxRequest request);
}

/// <summary>A single queued side effect awaiting delivery.</summary>
public sealed record OutboxRequest
{
    /// <summary>Template code, e.g. <c>order.placed</c>.</summary>
    public required string Type { get; init; }

    /// <summary>Serialized payload the delivery worker will render the template with.</summary>
    public required string Payload { get; init; }

    /// <summary>Optional de-duplication key — a replay with the same key is dropped.</summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>Delay delivery until this instant (used for booking reminders).</summary>
    public DateTimeOffset? NotBeforeUtc { get; init; }
}
