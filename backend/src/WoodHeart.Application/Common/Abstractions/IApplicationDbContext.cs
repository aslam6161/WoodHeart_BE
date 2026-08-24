namespace WoodHeart.Application.Common.Abstractions;

/// <summary>
/// The unit of work. Handlers call <see cref="SaveChangesAsync"/> exactly once,
/// at the end of the use case.
/// </summary>
/// <remarks>
/// Deliberately does NOT expose <c>DbSet&lt;T&gt;</c> or anything else from EF
/// Core — that would drag a persistence dependency into the Application layer
/// and break the dependency rule the architecture tests enforce. Data access
/// goes through per-aggregate repository ports instead.
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>
    /// Commits the current change set and dispatches domain events afterwards.
    /// </summary>
    /// <returns>The number of state entries written.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="operation"/> inside an explicit database transaction.
    /// Only needed when a use case must span more than one <c>SaveChanges</c>;
    /// the pipeline already wraps ordinary commands.
    /// </summary>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Base port for aggregate repositories.
/// </summary>
/// <remarks>
/// One repository per aggregate root, never one per table. There is no
/// <c>IOrderLineRepository</c> because an order line is only ever reachable
/// through its <c>Order</c>.
/// </remarks>
public interface IRepository<TAggregate> where TAggregate : class
{
    Task<TAggregate?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(TAggregate aggregate);

    void Update(TAggregate aggregate);

    void Remove(TAggregate aggregate);
}
