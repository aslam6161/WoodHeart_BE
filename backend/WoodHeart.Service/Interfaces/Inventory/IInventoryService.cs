using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Repository;
using WoodHeart.Service.DTOs.Inventory;
using WoodHeart.Service.DTOs.Ordering;

namespace WoodHeart.Service.Interfaces.Inventory;

/// <summary>
/// The stock ledger: what the order lifecycle does to it, and what the admin
/// does to it by hand.
/// </summary>
/// <remarks>
/// <para>
/// <b>The lifecycle methods stage; they do not commit.</b> Placing an order
/// reserves stock and writes the order in one transaction, and a hold that
/// committed on its own would survive a placement that rolled back. The
/// caller owns the unit of work. The admin methods are the opposite — each is
/// its own transaction — because there is no larger operation for them to be
/// part of.
/// </para>
/// <para>
/// Made-to-order and service products are skipped throughout. They have no
/// shelf; their lead time is on the order line instead.
/// </para>
/// </remarks>
public interface IInventoryService
{
    // --- The order lifecycle -------------------------------------------------

    /// <summary>
    /// Holds stock for every stocked line of a just-placed order. Fails with
    /// <c>ordering.insufficient_stock.conflict</c> naming the line that
    /// cannot be filled; nothing is held in that case.
    /// </summary>
    Task<GeneralResponse> ReserveForOrderAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// What a status change does to stock: shipping commits the holds as
    /// sales, cancelling releases them, a return puts the units back. Other
    /// moves do nothing. Idempotent — a hold is only ever settled once.
    /// </summary>
    Task ApplyStatusChangeAsync(
        Order order, OrderStatus from, OrderStatus to, string actor,
        CancellationToken cancellationToken = default);

    // --- The admin ------------------------------------------------------------

    Task<GeneralResponse<PagedResult<StockLevelDto>>> SearchAsync(
        StockQueryDto query, CancellationToken cancellationToken = default);

    Task<GeneralResponse<StockSummaryDto>> SummaryAsync(CancellationToken cancellationToken = default);

    Task<GeneralResponse<StockLevelDto>> GetLevelAsync(long variantId, CancellationToken cancellationToken = default);

    Task<GeneralResponse<PagedResult<StockMovementDto>>> GetMovementsAsync(
        long variantId, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// A movement by hand. Creates the count on the first stock-in. Answers
    /// with the level as it now stands.
    /// </summary>
    Task<GeneralResponse<StockLevelDto>> AdjustAsync(
        long variantId, AdjustStockDto dto, CancellationToken cancellationToken = default);
}
