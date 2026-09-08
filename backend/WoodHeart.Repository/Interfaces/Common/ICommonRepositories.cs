using WoodHeart.Domain.Entity.Common;

namespace WoodHeart.Repository.Interfaces.Common;

/// <summary>
/// The outbox. Services stage a message here in the same unit of work as the
/// business change that caused it; the delivery worker drains it.
/// </summary>
public interface IOutboxRepository : IRepository<OutboxMessage>
{
    /// <summary>
    /// Claims a batch of due messages for delivery.
    /// </summary>
    /// <remarks>
    /// Takes a row lock and skips rows another worker already holds, so running
    /// two workers doubles throughput instead of doubling the customer's SMS.
    /// </remarks>
    Task<IReadOnlyList<OutboxMessage>> ClaimDueBatchAsync(
        DateTimeOffset now, int batchSize, CancellationToken cancellationToken = default);

    Task<bool> ExistsByIdempotencyKeyAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns messages a stopped worker left mid-flight to the queue.
    /// </summary>
    /// <remarks>
    /// <b>Without this, a deploy loses notifications permanently.</b>
    /// <see cref="ClaimDueBatchAsync"/> selects only <c>Pending</c> rows, so a
    /// message marked <c>Processing</c> by a worker that was then killed — a
    /// restart, a container eviction, a crash — is never looked at again, and
    /// the customer is simply never told about their order.
    /// </remarks>
    Task<int> ReclaimStaleAsync(
        DateTimeOffset olderThan, CancellationToken cancellationToken = default);
}

public interface IStoreSettingRepository : IRepository<StoreSetting>
{
    Task<StoreSetting?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoreSetting>> GetByCategoryAsync(
        string category, CancellationToken cancellationToken = default);
}

public interface IFeatureFlagRepository : IRepository<FeatureFlag>
{
    Task<FeatureFlag?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>
/// Hands out the next value in a human-facing number series.
/// </summary>
/// <remarks>
/// One method, and it is a single statement against the database rather than a
/// read followed by a write. Two customers checking out in the same second must
/// not be given the same order number, and any implementation that reads a
/// value into memory before incrementing it will eventually do exactly that.
/// </remarks>
public interface INumberSequenceRepository : IRepository<NumberSequence>
{
    /// <summary>
    /// Reserves and returns the next value for a sequence within a period.
    /// </summary>
    /// <remarks>
    /// Creates the row on first use, so a new month needs no setup and no
    /// migration.
    /// <para>
    /// It runs inside whatever transaction the caller has open, and holds the
    /// row lock until that transaction ends. Order placement is therefore
    /// serialised on this one row — deliberately. At this shop's volume the
    /// wait is microseconds, and in exchange a placement that rolls back
    /// returns its number instead of burning it.
    /// </para>
    /// </remarks>
    Task<int> ReserveNextAsync(
        string name, string period, CancellationToken cancellationToken = default);
}
