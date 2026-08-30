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
