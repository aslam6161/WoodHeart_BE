namespace WoodHeart.Repository;

/// <summary>
/// The transaction boundary. One per request, injected into services.
/// </summary>
/// <remarks>
/// <para>
/// A service stages work through its repositories and then commits once, here.
/// The rule that follows is worth stating plainly because it is the whole point
/// of separating this from <see cref="IRepository{T}"/>: <b>a method that is
/// not the entry point of a use case must not commit.</b> If
/// <c>NotificationService.QueueAsync</c> saves, then every caller that queues a
/// notification mid-operation has silently committed whatever else it had
/// pending.
/// </para>
/// <para>
/// <see cref="ExecuteInTransactionAsync"/> is for the operations that span more
/// than one <see cref="SaveChangesAsync"/> call, or that must roll back on a
/// business failure rather than only on an exception — placing an order draws
/// down stock, writes an order, writes a payment record and queues an SMS, and
/// a partial version of that is worse than none of it.
/// </para>
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>Commits everything staged. Returns the number of rows affected.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="operation"/> inside an explicit transaction, joining
    /// one already in progress rather than nesting.
    /// </summary>
    /// <remarks>
    /// Wrapped in the provider's execution strategy, so a transient
    /// connection drop retries the whole unit instead of failing with
    /// "the configured execution strategy does not support user-initiated
    /// transactions" — the error every EF retry policy hits the first time it
    /// meets an explicit transaction.
    /// </remarks>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default);

    Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default);
}
