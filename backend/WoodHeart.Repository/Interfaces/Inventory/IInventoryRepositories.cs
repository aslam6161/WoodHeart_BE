using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.Entity.Inventory;

namespace WoodHeart.Repository.Interfaces.Inventory;

/// <summary>A variant and its count — the count being null until the first stock-in.</summary>
public sealed record StockLevel(ProductVariant Variant, StockItem? Item);

/// <summary>The counts, the ledger and the holds. One repository: they move together.</summary>
public interface IStockRepository : IRepository<StockItem>
{
    /// <summary>The count for one variant, tracked, or null if it has never been stocked.</summary>
    Task<StockItem?> GetByVariantAsync(long variantId, CancellationToken cancellationToken = default);

    /// <summary>The counts for several variants, tracked. Missing variants are simply absent.</summary>
    Task<IReadOnlyList<StockItem>> GetByVariantsAsync(
        IReadOnlyCollection<long> variantIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every stocked variant with its product and its count, for the admin
    /// stock list — including variants that have no count yet, which is the
    /// list's most useful row: the thing that cannot be sold until somebody
    /// stocks it.
    /// </summary>
    /// <param name="lowOnly">
    /// Only rows at or below their reorder level (or <paramref name="lowThreshold"/>
    /// where none is set), unstocked rows included.
    /// </param>
    Task<IReadOnlyList<StockLevel>> SearchAsync(
        string? term, bool lowOnly, int lowThreshold, int skip, int take,
        CancellationToken cancellationToken = default);

    Task<int> CountAsync(
        string? term, bool lowOnly, int lowThreshold, CancellationToken cancellationToken = default);

    /// <summary>
    /// Of the given variants, the ones whose product keeps stock — as
    /// opposed to made-to-order or a service. What the order lifecycle uses
    /// to know which lines have a shelf.
    /// </summary>
    Task<IReadOnlySet<long>> GetStockedVariantIdsAsync(
        IReadOnlyCollection<long> variantIds, CancellationToken cancellationToken = default);

    Task AddMovementAsync(StockMovement movement, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StockMovement>> GetMovementsAsync(
        long variantId, int skip, int take, CancellationToken cancellationToken = default);

    Task<int> CountMovementsAsync(long variantId, CancellationToken cancellationToken = default);

    Task AddReservationAsync(StockReservation reservation, CancellationToken cancellationToken = default);

    /// <summary>An order's holds, with their stock items, tracked.</summary>
    Task<IReadOnlyList<StockReservation>> GetReservationsForOrderAsync(
        long orderId, CancellationToken cancellationToken = default);
}
