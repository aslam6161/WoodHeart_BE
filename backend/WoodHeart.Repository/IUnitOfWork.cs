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
    /// <para>
    /// Wrapped in the provider's execution strategy, so a transient
    /// connection drop retries the whole unit instead of failing with
    /// "the configured execution strategy does not support user-initiated
    /// transactions" — the error every EF retry policy hits the first time it
    /// meets an explicit transaction.
    /// </para>
    /// <para>
    /// <b>A failed result rolls back.</b> When <typeparamref name="TResult"/>
    /// is a <see cref="GeneralResponse"/> and it says <c>IsSuccess == false</c>,
    /// the transaction is rolled back, whatever was saved inside it. A use
    /// case that reports failure has not happened; an order written, saved,
    /// and then refused for stock must not be in the database when the
    /// customer is told it was refused. Exceptions roll back as before.
    /// </para>
    /// </remarks>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default);

    Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default);
}
