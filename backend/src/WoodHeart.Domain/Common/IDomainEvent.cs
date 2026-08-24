namespace WoodHeart.Domain.Common;

/// <summary>
/// Something that has happened in the domain, expressed in past tense
/// (OrderPlaced, StockDepleted, BookingConfirmed).
/// </summary>
/// <remarks>
/// Domain events are how modules stay decoupled: <c>Ordering</c> never calls
/// into <c>Inventory</c>; it raises <c>OrderPlaced</c> and an Inventory handler
/// reacts. They are collected during <c>SaveChangesAsync</c> and dispatched
/// <em>after</em> the transaction commits, so a failing handler can never roll
/// back the business change that caused it.
/// </remarks>
public interface IDomainEvent
{
    /// <summary>Unique id of this occurrence — used for outbox de-duplication.</summary>
    Guid EventId { get; }

    /// <summary>When the event happened, in UTC.</summary>
    DateTimeOffset OccurredAtUtc { get; }
}

/// <summary>
/// Convenience base so concrete events stay one-liners:
/// <c>public sealed record OrderPlaced(Guid OrderId) : DomainEvent;</c>
/// </summary>
public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.CreateVersion7();

    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
