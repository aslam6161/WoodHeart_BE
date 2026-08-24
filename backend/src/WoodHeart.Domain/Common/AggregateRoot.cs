namespace WoodHeart.Domain.Common;

/// <summary>
/// An aggregate root is the ONLY entry point into its cluster of entities and
/// the only thing a repository ever loads or saves.
/// </summary>
/// <remarks>
/// Consequences we rely on throughout the codebase:
/// <list type="bullet">
///   <item>Nothing outside <c>Order</c> may mutate an <c>OrderLine</c>.</item>
///   <item>A transaction touches exactly one aggregate wherever possible;
///         cross-aggregate work happens through domain events.</item>
///   <item>Invariants (totals, stock arithmetic, status transitions) are
///         enforced by methods on the root, never by property setters.</item>
/// </list>
/// </remarks>
public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot() { }

    protected AggregateRoot(Guid id) : base(id) { }

    /// <summary>
    /// Optimistic concurrency token. Mapped to PostgreSQL's system <c>xmin</c>
    /// column, so no extra column is needed on the table.
    /// </summary>
    public uint Version { get; protected set; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /// <summary>Called by the persistence layer once events have been handed to the dispatcher.</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();
}
