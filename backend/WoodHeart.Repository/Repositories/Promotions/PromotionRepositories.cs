using Microsoft.EntityFrameworkCore;
using WoodHeart.Domain.Entity.Promotions;
using WoodHeart.Domain.Enums.Promotions;
using WoodHeart.Domain.Promotions;
using WoodHeart.Repository.Interfaces.Promotions;

namespace WoodHeart.Repository.Repositories.Promotions;

public class DiscountRepository(DataContext context)
    : Repository<Discount>(context), IDiscountRepository
{
    public async Task<IReadOnlyList<Discount>> GetCandidatesAsync(
        DateTimeOffset now,
        IReadOnlyCollection<string> codes,
        CancellationToken cancellationToken = default)
    {
        var normalised = codes
            .Select(Discount.NormaliseCode)
            .Where(code => code is not null)
            .Select(code => code!)
            .Distinct()
            .ToArray();

        // The live automatic promotions. Filtered in the database rather than
        // in the engine: a shop that has run promotions for two years has
        // hundreds of expired rows and none of them can affect this basket.
        var automatic = await WithTargets()
            .Where(x => x.Code == null
                        && x.Status == DiscountStatus.Active
                        && (x.StartsAt == null || x.StartsAt <= now)
                        && (x.EndsAt == null || x.EndsAt > now))
            .ToListAsync(cancellationToken);

        if (normalised.Length == 0)
        {
            return automatic;
        }

        // Typed codes come back in whatever state they are in, so the engine
        // can answer "that code ran until the 14th" rather than "no such code".
        var typed = await WithTargets()
            .Where(x => x.Code != null && normalised.Contains(x.Code))
            .ToListAsync(cancellationToken);

        return [.. automatic, .. typed];
    }

    public async Task<Discount?> GetByCodeAsync(
        string code, CancellationToken cancellationToken = default)
    {
        var normalised = Discount.NormaliseCode(code);

        return normalised is null
            ? null
            : await WithTargets().FirstOrDefaultAsync(x => x.Code == normalised, cancellationToken);
    }

    public async Task<Discount?> GetWithTargetsAsync(
        long id, CancellationToken cancellationToken = default) =>
        await Set.Include(x => x.Targets).ThenInclude(t => t.Category)
            .Include(x => x.Targets).ThenInclude(t => t.Product)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<PagedList<Discount>> SearchAsync(
        string? term,
        DiscountStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = Set.AsNoTracking().Include(x => x.Targets).AsSplitQuery().AsQueryable();

        if (status is { } wanted)
        {
            query = query.Where(x => x.Status == wanted);
        }

        if (!string.IsNullOrWhiteSpace(term))
        {
            var pattern = $"%{term.Trim()}%";

            query = query.Where(x =>
                EF.Functions.ILike(x.Name, pattern)
                || (x.Code != null && EF.Functions.ILike(x.Code, pattern)));
        }

        // Newest first: the thing an admin wants is nearly always the one they
        // just made.
        return await PagedList<Discount>.CreateAsync(
            query.OrderByDescending(x => x.Id), page, pageSize, cancellationToken);
    }

    public async Task<bool> CodeExistsAsync(
        string code, long? excludeId = null, CancellationToken cancellationToken = default)
    {
        var normalised = Discount.NormaliseCode(code);

        return normalised is not null
               && await Set.AnyAsync(
                   x => x.Code == normalised && (excludeId == null || x.Id != excludeId),
                   cancellationToken);
    }

    /// <summary>
    /// A discount with everything the engine reads, in one round trip.
    /// </summary>
    /// <remarks>
    /// The target's category is included for its <c>MaterializedPath</c>, which
    /// is how "20% off Bedroom" reaches a bed filed under Bedroom → Beds.
    /// Tracked, because the placement path writes usage rows against these.
    /// </remarks>
    private IQueryable<Discount> WithTargets() =>
        Set.Include(x => x.Targets).ThenInclude(target => target.Category).AsSplitQuery();
}

public class PromotionUsageRepository(DataContext context)
    : Repository<PromotionUsage>(context), IPromotionUsageRepository
{
    public async Task<IReadOnlyDictionary<long, DiscountUsage>> GetUsageAsync(
        IReadOnlyCollection<long> discountIds,
        long? customerId,
        string? contactPhone,
        CancellationToken cancellationToken = default)
    {
        if (discountIds.Count == 0)
        {
            return new Dictionary<long, DiscountUsage>();
        }

        var ids = discountIds.Distinct().ToArray();

        var rows = await Set.AsNoTracking()
            .Where(x => ids.Contains(x.DiscountId))
            .GroupBy(x => x.DiscountId)
            .Select(group => new
            {
                DiscountId = group.Key,
                Total = group.Count(),

                // "This customer" is the account when there is one and the
                // phone number when there is not, because most buyers here
                // never make an account and a per-customer limit that only
                // counted members would be no limit at all.
                ByCustomer = group.Count(x =>
                    (customerId != null && x.CustomerId == customerId)
                    || (contactPhone != null && x.ContactPhone == contactPhone))
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.DiscountId, row => new DiscountUsage(row.Total, row.ByCustomer));
    }

    public async Task<PagedList<PromotionUsage>> SearchAsync(
        long discountId, int page, int pageSize, CancellationToken cancellationToken = default) =>
        await PagedList<PromotionUsage>.CreateAsync(
            Set.AsNoTracking()
                .Include(x => x.Order)
                .Where(x => x.DiscountId == discountId)
                .OrderByDescending(x => x.UsedAt)
                .ThenByDescending(x => x.Id),
            page,
            pageSize,
            cancellationToken);

    /// <summary>
    /// What one discount has cost, summed in memory.
    /// </summary>
    /// <remarks>
    /// <c>Money</c> reaches the database through a value converter, and a
    /// converted property cannot be summed in SQL — <c>x.Amount.Amount</c> has
    /// no translation. The rows are bounded by how many orders used the
    /// discount, which is a number the shop chose when it set the usage limit,
    /// and this feeds one admin screen rather than the storefront. The day a
    /// discount has a hundred thousand redemptions, this becomes a raw query.
    /// </remarks>
    public async Task<(int Orders, decimal Total)> TotalsAsync(
        long discountId, CancellationToken cancellationToken = default)
    {
        var amounts = await Set.AsNoTracking()
            .Where(x => x.DiscountId == discountId)
            .Select(x => x.Amount)
            .ToListAsync(cancellationToken);

        return (amounts.Count, amounts.Sum(amount => amount.Amount));
    }
}
