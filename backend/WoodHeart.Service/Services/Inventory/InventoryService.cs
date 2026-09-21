using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.Entity.Identity;
using WoodHeart.Domain.Entity.Inventory;
using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Enums.Catalog;
using WoodHeart.Domain.Enums.Inventory;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.Exceptions;
using WoodHeart.Domain.Helpers;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Catalog;
using WoodHeart.Repository.Interfaces.Inventory;
using WoodHeart.Repository.Interfaces.Ordering;
using WoodHeart.Service.DTOs.Inventory;
using WoodHeart.Service.DTOs.Ordering;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Inventory;

namespace WoodHeart.Service.Services.Inventory;

/// <summary>
/// The stock ledger, from both sides: the order lifecycle and the admin.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two orders drawing down the last bed at the same instant is the case
/// this is built around.</b> Both read Available = 1, both reserve, and
/// without a guard both succeed. The guard is the row version: every
/// <see cref="StockItem"/> carries PostgreSQL's <c>xmin</c>, and the second
/// commit fails with a concurrency exception that the pipeline turns into
/// a 409 — the customer is asked to try again, and the retry sees 0. No
/// explicit lock, no serializable transaction; the database already keeps
/// the version.
/// </para>
/// <para>
/// Every count change writes a movement first. There is no method here that
/// touches <c>OnHand</c> directly, and the entity offers none.
/// </para>
/// </remarks>
public class InventoryService(
    IStockRepository stock,
    IProductVariantRepository variants,
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    IStoreSettingService settings,
    ICurrentUserService currentUser,
    UserManager<AppUser> users,
    IDateTimeProvider clock,
    ILogger<InventoryService> logger) : IInventoryService
{
    private const string SystemActor = "System";

    // -------------------------------------------------------------------------
    // The order lifecycle
    // -------------------------------------------------------------------------

    public async Task<GeneralResponse> ReserveForOrderAsync(
        Order order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        var variantIds = order.Lines.Select(l => l.ProductVariantId).Distinct().ToList();
        var stocked = await stock.GetStockedVariantIdsAsync(variantIds, cancellationToken);

        if (stocked.Count == 0)
        {
            return GeneralResponse.Success();
        }

        var items = (await stock.GetByVariantsAsync(stocked.ToList(), cancellationToken))
            .ToDictionary(x => x.ProductVariantId);

        foreach (var line in order.Lines.Where(l => stocked.Contains(l.ProductVariantId)))
        {
            // Never stocked means none, not unlimited. A variant that has
            // never been received cannot have one on the shelf.
            if (!items.TryGetValue(line.ProductVariantId, out var item))
            {
                return InsufficientStock(line, available: 0);
            }

            try
            {
                item.Reserve(line.Quantity);
            }
            catch (DomainException ex) when (ex.Code == InventoryErrors.InsufficientStock)
            {
                // Nothing staged so far is committed — the caller's
                // transaction rolls the earlier holds back with it.
                return InsufficientStock(line, item.Available);
            }

            stock.Update(item);

            await stock.AddReservationAsync(new StockReservation
            {
                StockItem = item,
                StockItemId = item.Id,
                ProductVariantId = line.ProductVariantId,
                OrderId = order.Id,
                Quantity = line.Quantity
            }, cancellationToken);

            InventoryLog.Reserved(logger, line.Quantity, line.Sku, order.OrderNumber, item.Available);
        }

        return GeneralResponse.Success();
    }

    public async Task ApplyStatusChangeAsync(
        Order order, OrderStatus from, OrderStatus to, string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        // Only three moves touch the shelf. Everything else — confirming,
        // packing, delivering, completing — is paperwork as far as the count
        // is concerned.
        switch (to)
        {
            case OrderStatus.Shipped:
                await CommitAsync(order, actor, cancellationToken);
                break;

            case OrderStatus.Cancelled:
                await ReleaseAsync(order, cancellationToken);
                break;

            case OrderStatus.Returned:
                await ReturnAsync(order, actor, cancellationToken);
                break;
        }
    }

    /// <summary>The van left: the held units become sales.</summary>
    private async Task CommitAsync(Order order, string actor, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        foreach (var hold in await ActiveHoldsAsync(order, cancellationToken))
        {
            var movement = hold.StockItem.Commit(hold.Quantity, order.Id, actor, now);

            hold.Status = StockReservationStatus.Committed;
            hold.ResolvedAt = now;

            await stock.AddMovementAsync(movement, cancellationToken);
            stock.Update(hold.StockItem);

            InventoryLog.Moved(logger, movement.Type.ToString(), movement.Quantity, hold.ProductVariantId,
                movement.OnHandAfter, actor);
        }
    }

    /// <summary>The order died before shipping: the units are available again.</summary>
    private async Task ReleaseAsync(Order order, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        foreach (var hold in await ActiveHoldsAsync(order, cancellationToken))
        {
            hold.StockItem.Release(hold.Quantity);
            hold.Status = StockReservationStatus.Released;
            hold.ResolvedAt = now;

            stock.Update(hold.StockItem);

            InventoryLog.Released(logger, hold.Quantity, hold.ProductVariantId, order.OrderNumber);
        }
    }

    /// <summary>
    /// The goods came back: a Return movement per line that was sold.
    /// Reads the committed holds, so a line that was never stocked — or an
    /// order that was cancelled before it shipped — puts nothing back.
    /// </summary>
    private async Task ReturnAsync(Order order, string actor, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var holds = await stock.GetReservationsForOrderAsync(order.Id, cancellationToken);

        foreach (var hold in holds.Where(h => h.Status == StockReservationStatus.Committed))
        {
            var movement = hold.StockItem.Apply(
                StockMovementType.Return, hold.Quantity, actor, now,
                reason: $"Returned with order {order.OrderNumber}.", orderId: order.Id);

            await stock.AddMovementAsync(movement, cancellationToken);
            stock.Update(hold.StockItem);

            InventoryLog.Moved(logger, movement.Type.ToString(), movement.Quantity, hold.ProductVariantId,
                movement.OnHandAfter, actor);
        }
    }

    private async Task<IReadOnlyList<StockReservation>> ActiveHoldsAsync(
        Order order, CancellationToken cancellationToken) =>
        [.. (await stock.GetReservationsForOrderAsync(order.Id, cancellationToken))
            .Where(h => h.Status == StockReservationStatus.Active)];

    private static GeneralResponse InsufficientStock(OrderLine line, int available) =>
        GeneralResponse.Invalid(
            InventoryErrors.InsufficientStock,
            available <= 0
                ? $"{line.ProductNameEn} ({line.VariantName}) has just sold out."
                : $"Only {available} of {line.ProductNameEn} ({line.VariantName}) left; you asked for {line.Quantity}.",
            new Dictionary<string, string[]>
            {
                [line.ProductVariantId.ToString(CultureInfo.InvariantCulture)] =
                    [available <= 0 ? "Sold out." : $"Only {available} left."]
            });

    // -------------------------------------------------------------------------
    // The admin
    // -------------------------------------------------------------------------

    public async Task<GeneralResponse<PagedResult<StockLevelDto>>> SearchAsync(
        StockQueryDto query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var threshold = await ThresholdAsync(cancellationToken);
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var total = await stock.CountAsync(query.Term, query.LowOnly, threshold, cancellationToken);
        var rows = await stock.SearchAsync(
            query.Term, query.LowOnly, threshold, (page - 1) * pageSize, pageSize, cancellationToken);

        return GeneralResponse<PagedResult<StockLevelDto>>.Success(new PagedResult<StockLevelDto>
        {
            Items = [.. rows.Select(row => ToLevel(row.Variant, row.Item, threshold))],
            Total = total,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<GeneralResponse<StockSummaryDto>> SummaryAsync(CancellationToken cancellationToken = default)
    {
        var threshold = await ThresholdAsync(cancellationToken);

        // Three counts, three cheap queries. The low count includes the
        // unstocked ones — see the repository — so the unstocked figure is
        // taken separately for the message that says which is which.
        var all = await stock.CountAsync(null, lowOnly: false, threshold, cancellationToken);
        var low = await stock.CountAsync(null, lowOnly: true, threshold, cancellationToken);
        var stockedIds = await stock.GetAllAsync(cancellationToken);

        return GeneralResponse<StockSummaryDto>.Success(new StockSummaryDto
        {
            StockedVariants = all,
            LowVariants = low,
            UnstockedVariants = Math.Max(all - stockedIds.Count, 0)
        });
    }

    public async Task<GeneralResponse<StockLevelDto>> GetLevelAsync(
        long variantId, CancellationToken cancellationToken = default)
    {
        var variant = await variants.GetWithProductAsync(variantId, cancellationToken);

        if (variant is null)
        {
            return GeneralResponse<StockLevelDto>.Fail(InventoryErrors.VariantNotFound, "No such variant.");
        }

        var item = await stock.GetByVariantAsync(variantId, cancellationToken);
        var threshold = await ThresholdAsync(cancellationToken);

        return GeneralResponse<StockLevelDto>.Success(ToLevel(variant, item, threshold));
    }

    public async Task<GeneralResponse<PagedResult<StockMovementDto>>> GetMovementsAsync(
        long variantId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var total = await stock.CountMovementsAsync(variantId, cancellationToken);
        var rows = await stock.GetMovementsAsync(variantId, (page - 1) * pageSize, pageSize, cancellationToken);

        // The order numbers, for the ledger's "Sale — WH-2609-00042" lines.
        var orderIds = rows.Where(m => m.OrderId is not null).Select(m => m.OrderId!.Value).Distinct().ToList();
        var numbers = orderIds.Count == 0
            ? new Dictionary<long, string>()
            : await orders.GetNumbersAsync(orderIds, cancellationToken);

        return GeneralResponse<PagedResult<StockMovementDto>>.Success(new PagedResult<StockMovementDto>
        {
            Items = [.. rows.Select(m => new StockMovementDto
            {
                Id = m.Id,
                Type = m.Type,
                Quantity = m.Quantity,
                OnHandAfter = m.OnHandAfter,
                OrderId = m.OrderId,
                OrderNumber = m.OrderId is { } id && numbers.TryGetValue(id, out var number) ? number : null,
                Reference = m.Reference,
                Reason = m.Reason,
                PerformedBy = m.PerformedBy,
                OccurredAt = m.OccurredAt
            })],
            Total = total,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<GeneralResponse<StockLevelDto>> AdjustAsync(
        long variantId, AdjustStockDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var variant = await variants.GetWithProductAsync(variantId, cancellationToken);

        if (variant is null)
        {
            return GeneralResponse<StockLevelDto>.Fail(InventoryErrors.VariantNotFound, "No such variant.");
        }

        if (variant.Product.ProductType != ProductType.Stocked)
        {
            return GeneralResponse<StockLevelDto>.Fail(
                InventoryErrors.VariantNotStocked,
                "This product is not kept in stock, so there is no count to change.");
        }

        if (dto.Type == StockMovementType.Sale)
        {
            return GeneralResponse<StockLevelDto>.Fail(
                InventoryErrors.TypeInvalid, "A sale is recorded by shipping the order.");
        }

        // Damage and corrections are the movements somebody will ask about.
        // "Damage, 2" with no reason is the ledger line that cannot be
        // explained in six months.
        if (dto.Type is StockMovementType.Adjustment or StockMovementType.Damage
            && string.IsNullOrWhiteSpace(dto.Reason))
        {
            return GeneralResponse<StockLevelDto>.Fail(
                InventoryErrors.ReasonRequired, "Please say why.");
        }

        var item = await stock.GetByVariantAsync(variantId, cancellationToken);
        var created = false;

        if (item is null)
        {
            // The first stock-in creates the count. Until then the variant is
            // "unstocked" rather than "zero" — the list shows the difference.
            item = new StockItem { ProductVariantId = variantId, OnHand = 0, Reserved = 0 };
            created = true;
        }

        var actor = await ActorNameAsync();

        StockMovement movement;

        try
        {
            movement = item.Apply(
                dto.Type, dto.Quantity, actor, clock.UtcNow, dto.Reason?.Trim(), dto.Reference?.Trim());
        }
        catch (DomainException ex)
        {
            return GeneralResponse<StockLevelDto>.Fail(ex.Code, ex.Message);
        }

        if (dto.ReorderLevel is { } level)
        {
            item.ReorderLevel = level;
        }

        if (created)
        {
            await stock.InsertAsync(item, cancellationToken);
        }
        else
        {
            stock.Update(item);
        }

        await stock.AddMovementAsync(movement, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        InventoryLog.Moved(logger, movement.Type.ToString(), movement.Quantity, variantId, movement.OnHandAfter, actor);

        var threshold = await ThresholdAsync(cancellationToken);

        return GeneralResponse<StockLevelDto>.Success(
            ToLevel(variant, item, threshold), $"Stock is now {item.OnHand}.");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<int> ThresholdAsync(CancellationToken cancellationToken) =>
        await settings.GetIntAsync(SettingKeys.LowStockThreshold, fallback: 5, cancellationToken);

    private async Task<string> ActorNameAsync()
    {
        if (currentUser.UserId is not { } userId)
        {
            return SystemActor;
        }

        var user = await users.FindByIdAsync(userId.ToString(CultureInfo.InvariantCulture));

        return user?.FullName is { Length: > 0 } name
            ? name
            : user?.UserName is { Length: > 0 } handle
                ? handle
                : "Staff";
    }

    private static StockLevelDto ToLevel(ProductVariant variant, StockItem? item, int threshold)
    {
        var product = variant.Product;
        var available = item?.Available ?? 0;
        var level = item?.ReorderLevel ?? threshold;

        return new StockLevelDto
        {
            VariantId = variant.Id,
            ProductId = variant.ProductId,
            ProductName = product?.Name.En ?? string.Empty,
            ProductSlug = product?.Slug.Value ?? string.Empty,
            Sku = variant.Sku,
            VariantName = variant.VariantName,
            ImagePath = variant.Media.FirstOrDefault(m => m.IsPrimary)?.StoragePath
                        ?? product?.Media.FirstOrDefault(m => m.IsPrimary)?.StoragePath,
            IsStocked = item is not null,
            OnHand = item?.OnHand ?? 0,
            Reserved = item?.Reserved ?? 0,
            Available = available,
            ReorderLevel = item?.ReorderLevel,
            IsLow = item is null || available <= level,
            LastMovementAt = item?.UpdatedAt ?? item?.CreatedAt
        };
    }
}

internal static partial class InventoryLog
{
    [LoggerMessage(
        EventId = 1900,
        Level = LogLevel.Information,
        Message = "Reserved {Quantity} x {Sku} for {OrderNumber}; {Available} still available.")]
    public static partial void Reserved(
        ILogger logger, int quantity, string sku, string orderNumber, int available);

    [LoggerMessage(
        EventId = 1901,
        Level = LogLevel.Information,
        Message = "Released {Quantity} of variant {VariantId} held by {OrderNumber}.")]
    public static partial void Released(ILogger logger, int quantity, long variantId, string orderNumber);

    /// <summary>Every ledger line, as a log line. The ledger is the record; this is the trace.</summary>
    [LoggerMessage(
        EventId = 1902,
        Level = LogLevel.Information,
        Message = "Stock {Type} {Quantity:+#;-#;0} on variant {VariantId}; on hand now {OnHandAfter}. By {Actor}.")]
    public static partial void Moved(
        ILogger logger, string type, int quantity, long variantId, int onHandAfter, string actor);
}
