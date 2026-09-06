using Microsoft.EntityFrameworkCore;
using WoodHeart.Domain.Entity.Common;
using WoodHeart.Domain.Enums.Common;
using WoodHeart.Repository.Interfaces.Common;

namespace WoodHeart.Repository.Repositories.Common;

public class OutboxRepository(DataContext context)
    : Repository<OutboxMessage>(context), IOutboxRepository
{
    public async Task<IReadOnlyList<OutboxMessage>> ClaimDueBatchAsync(
        DateTimeOffset now, int batchSize, CancellationToken cancellationToken = default)
    {
        // FOR UPDATE SKIP LOCKED is the point of this query. Without it, two
        // delivery workers select the same rows and the customer gets the same
        // SMS twice — which in this market is a duplicate charge on the gateway
        // invoice, not just an annoyance.
        var claimed = await Set
            .FromSqlRaw(
                """
                SELECT * FROM outbox_messages
                WHERE status = 'Pending'
                  AND (not_before IS NULL OR not_before <= {0})
                  AND (next_attempt_at IS NULL OR next_attempt_at <= {0})
                ORDER BY created_at
                LIMIT {1}
                FOR UPDATE SKIP LOCKED
                """,
                now,
                batchSize)
            .ToListAsync(cancellationToken);

        foreach (var message in claimed)
        {
            message.Status = OutboxStatus.Processing;
            message.AttemptCount++;
        }

        return claimed;
    }

    public async Task<bool> ExistsByIdempotencyKeyAsync(
        string key, CancellationToken cancellationToken = default) =>
        await Set.AnyAsync(x => x.IdempotencyKey == key, cancellationToken);
}

public class StoreSettingRepository(DataContext context)
    : Repository<StoreSetting>(context), IStoreSettingRepository
{
    public async Task<StoreSetting?> GetByKeyAsync(
        string key, CancellationToken cancellationToken = default) =>
        await Set.FirstOrDefaultAsync(x => x.Key == key, cancellationToken);

    public async Task<IReadOnlyList<StoreSetting>> GetByCategoryAsync(
        string category, CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking()
            .Where(x => x.Category == category)
            .OrderBy(x => x.Key)
            .ToListAsync(cancellationToken);
}

public class FeatureFlagRepository(DataContext context)
    : Repository<FeatureFlag>(context), IFeatureFlagRepository
{
    public async Task<FeatureFlag?> GetByNameAsync(
        string name, CancellationToken cancellationToken = default) =>
        await Set.FirstOrDefaultAsync(x => x.Name == name, cancellationToken);
}

public class NumberSequenceRepository(DataContext context)
    : Repository<NumberSequence>(context), INumberSequenceRepository
{
    public async Task<int> ReserveNextAsync(
        string name, string period, CancellationToken cancellationToken = default)
    {
        // One statement, and that is the whole design. The tempting version —
        // read the row, add one, save — hands two customers checking out in the
        // same second the same order number, and the unique index then fails a
        // real sale at its last step.
        //
        // ON CONFLICT makes the first order of a new month need no setup: the
        // row is created and returns 1. Without it, September works and October
        // throws at one minute past midnight.
        var values = await Context.Database
            .SqlQueryRaw<int>(
                """
                INSERT INTO number_sequences (name, period, next_value, created_at)
                VALUES ({0}, {1}, 1, now())
                ON CONFLICT (name, period) DO UPDATE
                    SET next_value = number_sequences.next_value + 1,
                        updated_at = now()
                RETURNING next_value AS "Value"
                """,
                name,
                period)
            .ToListAsync(cancellationToken);

        return values.Count > 0
            ? values[0]
            : throw new InvalidOperationException(
                $"Number sequence '{name}' for period '{period}' returned no value.");
    }
}
