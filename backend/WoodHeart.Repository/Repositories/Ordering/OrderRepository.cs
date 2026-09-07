using Microsoft.EntityFrameworkCore;
using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Repository.Interfaces.Ordering;

namespace WoodHeart.Repository.Repositories.Ordering;

public class OrderRepository(DataContext context)
    : Repository<Order>(context), IOrderRepository
{
    public async Task<Order?> GetByNumberAsync(
        string orderNumber, CancellationToken cancellationToken = default) =>
        await WithDetail().FirstOrDefaultAsync(x => x.OrderNumber == orderNumber, cancellationToken);

    public async Task<Order?> GetDetailAsync(long id, CancellationToken cancellationToken = default) =>
        await WithDetail().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Order>> GetForCustomerAsync(
        long customerId, int skip, int take, CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking()
            .Include(x => x.Lines.OrderBy(line => line.Id))
            .Where(x => x.CustomerId == customerId)
            .OrderByDescending(x => x.PlacedAt)
            .ThenByDescending(x => x.Id)
            .Skip(skip)
            .Take(take)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    public async Task<int> CountForCustomerAsync(
        long customerId, CancellationToken cancellationToken = default) =>
        await Set.CountAsync(x => x.CustomerId == customerId, cancellationToken);

    public async Task<Order?> GetByIdempotencyKeyAsync(
        string idempotencyKey, CancellationToken cancellationToken = default) =>
        await WithDetail().FirstOrDefaultAsync(
            x => x.IdempotencyKey == idempotencyKey, cancellationToken);

    public async Task<IReadOnlyList<Order>> GetUnclaimedForPhoneAsync(
        string contactPhone, CancellationToken cancellationToken = default) =>
        await Set
            .Where(x => x.ContactPhone == contactPhone && x.CustomerId == null)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Order>> SearchAsync(
        OrderStatus? status,
        string? term,
        int skip,
        int take,
        CancellationToken cancellationToken = default) =>
        await Filtered(status, term)
            .AsNoTracking()
            .OrderByDescending(x => x.PlacedAt)
            .ThenByDescending(x => x.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<int> CountAsync(
        OrderStatus? status, string? term, CancellationToken cancellationToken = default) =>
        await Filtered(status, term).CountAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<OrderStatus, int>> CountByStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await Set.AsNoTracking()
            .GroupBy(x => x.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.Status, row => row.Count);
    }

    /// <summary>
    /// The admin list's filter: a status, and a search over the three things
    /// staff have in front of them when someone phones up.
    /// </summary>
    /// <remarks>
    /// Order number, phone and name — nothing else. Searching the address or
    /// the lines sounds helpful and turns the query into a sequential scan of a
    /// table that only grows.
    /// </remarks>
    private IQueryable<Order> Filtered(OrderStatus? status, string? term)
    {
        var query = Set.AsQueryable();

        if (status is { } wanted)
        {
            query = query.Where(x => x.Status == wanted);
        }

        if (!string.IsNullOrWhiteSpace(term))
        {
            var pattern = $"%{term.Trim()}%";

            query = query.Where(x =>
                EF.Functions.ILike(x.OrderNumber, pattern)
                || EF.Functions.ILike(x.ContactPhone, pattern)
                || EF.Functions.ILike(x.ContactName, pattern));
        }

        return query;
    }

    /// <summary>
    /// An order with everything a person looking at it needs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Tracked, because every caller of this is about to move the order's
    /// status or record a payment against it.
    /// </para>
    /// <para>
    /// No include of the product or variant. Every field a line displays is
    /// already copied onto it, which is the whole point of snapshotting — and
    /// it means an order still renders correctly after a product has been
    /// withdrawn from sale.
    /// </para>
    /// </remarks>
    private IQueryable<Order> WithDetail() =>
        Set.Include(x => x.Lines.OrderBy(line => line.Id))
            .Include(x => x.Timeline.OrderBy(entry => entry.OccurredAt).ThenBy(entry => entry.Id))
            .AsSplitQuery();
}
