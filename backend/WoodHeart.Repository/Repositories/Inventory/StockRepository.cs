using Microsoft.EntityFrameworkCore;
using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.Entity.Inventory;
using WoodHeart.Domain.Enums.Catalog;
using WoodHeart.Repository.Interfaces.Inventory;

namespace WoodHeart.Repository.Repositories.Inventory;

public class StockRepository(DataContext context)
    : Repository<StockItem>(context), IStockRepository
{
    public async Task<StockItem?> GetByVariantAsync(
        long variantId, CancellationToken cancellationToken = default) =>
        await Set.FirstOrDefaultAsync(x => x.ProductVariantId == variantId, cancellationToken);

    public async Task<IReadOnlyList<StockItem>> GetByVariantsAsync(
        IReadOnlyCollection<long> variantIds, CancellationToken cancellationToken = default) =>
        await Set.Where(x => variantIds.Contains(x.ProductVariantId)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<StockLevel>> SearchAsync(
        string? term, bool lowOnly, int lowThreshold, int skip, int take,
        CancellationToken cancellationToken = default) =>
        await Levels(term, lowOnly, lowThreshold)
            // By product code, then variant order: Name is a jsonb
            // LocalizedText that EF cannot order by, and the code is what
            // the shop's own paperwork sorts on.
            .OrderBy(v => v.Product.Code)
            .ThenBy(v => v.SortOrder)
            .ThenBy(v => v.Id)
            .Skip(skip)
            .Take(take)
            .Select(v => new StockLevel(v, v.Stock))
            .ToListAsync(cancellationToken);

    public async Task<int> CountAsync(
        string? term, bool lowOnly, int lowThreshold, CancellationToken cancellationToken = default) =>
        await Levels(term, lowOnly, lowThreshold).CountAsync(cancellationToken);

    public async Task<IReadOnlySet<long>> GetStockedVariantIdsAsync(
        IReadOnlyCollection<long> variantIds, CancellationToken cancellationToken = default)
    {
        var ids = await Context.Set<ProductVariant>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(v => variantIds.Contains(v.Id) && v.Product.ProductType == ProductType.Stocked)
            .Select(v => v.Id)
            .ToListAsync(cancellationToken);

        return ids.ToHashSet();
    }

    public async Task AddMovementAsync(StockMovement movement, CancellationToken cancellationToken = default) =>
        await Context.Set<StockMovement>().AddAsync(movement, cancellationToken);

    public async Task<IReadOnlyList<StockMovement>> GetMovementsAsync(
        long variantId, int skip, int take, CancellationToken cancellationToken = default) =>
        await Context.Set<StockMovement>()
            .AsNoTracking()
            .Where(x => x.ProductVariantId == variantId)
            .OrderByDescending(x => x.OccurredAt)
            .ThenByDescending(x => x.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<int> CountMovementsAsync(long variantId, CancellationToken cancellationToken = default) =>
        await Context.Set<StockMovement>().CountAsync(x => x.ProductVariantId == variantId, cancellationToken);

    public async Task AddReservationAsync(
        StockReservation reservation, CancellationToken cancellationToken = default) =>
        await Context.Set<StockReservation>().AddAsync(reservation, cancellationToken);

    public async Task<IReadOnlyList<StockReservation>> GetReservationsForOrderAsync(
        long orderId, CancellationToken cancellationToken = default) =>
        await Context.Set<StockReservation>()
            .Include(x => x.StockItem)
            .Where(x => x.OrderId == orderId)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The stock list's rows: active variants of stocked products, joined to
    /// their count if they have one. Built on the variant rather than the
    /// count so that a variant nobody has stocked yet is a row, not a gap.
    /// </summary>
    private IQueryable<ProductVariant> Levels(string? term, bool lowOnly, int lowThreshold)
    {
        var query = Context.Set<ProductVariant>()
            .AsNoTracking()
            .Include(v => v.Product)
            .Include(v => v.Stock)
            .Where(v => v.IsActive && v.Product.ProductType == ProductType.Stocked);

        if (!string.IsNullOrWhiteSpace(term))
        {
            var pattern = $"%{term.Trim()}%";

            // SearchText, not Name — see ProductRepository for why a jsonb
            // LocalizedText cannot be searched directly.
            query = query.Where(v =>
                EF.Functions.ILike(v.Sku, pattern)
                || EF.Functions.ILike(v.VariantName, pattern)
                || EF.Functions.ILike(v.Product.SearchText, pattern));
        }

        if (lowOnly)
        {
            // Unstocked counts as low — lower than low. Then the item's own
            // reorder level, or the store's threshold where it has none.
            query = query.Where(v =>
                v.Stock == null
                || v.Stock.OnHand - v.Stock.Reserved <= (v.Stock.ReorderLevel ?? lowThreshold));
        }

        return query;
    }
}
