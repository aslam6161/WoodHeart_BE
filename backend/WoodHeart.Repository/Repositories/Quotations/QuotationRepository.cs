using Microsoft.EntityFrameworkCore;
using WoodHeart.Domain.Entity.Quotations;
using WoodHeart.Domain.Enums.Quotations;
using WoodHeart.Repository.Interfaces.Quotations;

namespace WoodHeart.Repository.Repositories.Quotations;

public class QuotationRepository(DataContext context)
    : Repository<Quotation>(context), IQuotationRepository
{
    public async Task<Quotation?> GetByNumberAsync(
        string quotationNumber, CancellationToken cancellationToken = default) =>
        await WithDetail()
            .FirstOrDefaultAsync(x => x.QuotationNumber == quotationNumber, cancellationToken);

    public async Task<Quotation?> GetDetailAsync(
        long id, CancellationToken cancellationToken = default) =>
        await WithDetail().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    /// <summary>
    /// The board.
    /// </summary>
    /// <remarks>
    /// Newest first, which is the opposite of the booking board and right for
    /// the same reason it is right there: a diary is read forwards because the
    /// appointment is ahead of you, and a quotation list is read backwards
    /// because the one somebody is asking about is nearly always the last one
    /// written.
    /// </remarks>
    public async Task<PagedList<Quotation>> SearchAsync(
        QuotationSearch criteria, CancellationToken cancellationToken = default)
    {
        // The booking and the order are included because the board draws a
        // column for each. Without them the row renders blank where the order
        // number belongs, which reads as a quotation that became nothing.
        var query = Set.AsNoTracking()
            .Include(x => x.Lines)
            .Include(x => x.Booking)
            .Include(x => x.ConvertedOrder)
            .AsSplitQuery()
            .AsQueryable();

        if (criteria.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        if (criteria.BookingId is { } bookingId)
        {
            query = query.Where(x => x.BookingId == bookingId);
        }

        if (!string.IsNullOrWhiteSpace(criteria.Term))
        {
            var pattern = $"%{criteria.Term.Trim()}%";

            query = query.Where(x =>
                EF.Functions.ILike(x.QuotationNumber, pattern)
                || EF.Functions.ILike(x.ContactName, pattern)
                || EF.Functions.ILike(x.ContactPhone, pattern));
        }

        return await PagedList<Quotation>.CreateAsync(
            query.OrderByDescending(x => x.Id), criteria.Page, criteria.PageSize, cancellationToken);
    }

    public async Task<IReadOnlyList<Quotation>> GetForCustomerAsync(
        long customerId, int skip, int take, CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking()
            .Include(x => x.Lines)
            .Include(x => x.Booking)
            // So a customer's own list can say which order a quotation became,
            // rather than leaving them to guess that it became one at all.
            .Include(x => x.ConvertedOrder)
            .AsSplitQuery()
            .Where(x => x.CustomerId == customerId && x.Status != QuotationStatus.Draft)
            .OrderByDescending(x => x.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    /// <remarks>
    /// Drafts are excluded: a quotation nobody has sent is the designer's
    /// working copy, and a customer seeing half-finished figures is worse than
    /// seeing nothing.
    /// </remarks>
    public async Task<int> CountForCustomerAsync(
        long customerId, CancellationToken cancellationToken = default) =>
        await Set.CountAsync(
            x => x.CustomerId == customerId && x.Status != QuotationStatus.Draft, cancellationToken);

    public async Task<IReadOnlyList<Quotation>> GetLapsedAsync(
        DateOnly todayInDhaka, int take, CancellationToken cancellationToken = default) =>
        await Set
            .Where(x => x.Status == QuotationStatus.Sent && x.ValidUntil < todayInDhaka)
            .OrderBy(x => x.ValidUntil)
            .Take(take)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// A quotation with everything a screen renders, in one round trip.
    /// </summary>
    /// <remarks>
    /// Tracked, because every caller that reads one detail either edits it or
    /// moves it along. The lines are ordered here rather than in the mapper so
    /// that the order a designer put them in survives the round trip.
    /// </remarks>
    private IQueryable<Quotation> WithDetail() =>
        Set.Include(x => x.Lines.OrderBy(line => line.SortOrder).ThenBy(line => line.Id))
            .Include(x => x.Booking)
            .Include(x => x.ConvertedOrder)
            .AsSplitQuery();
}
