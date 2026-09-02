using Microsoft.EntityFrameworkCore;
using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Repository.Interfaces.Ordering;

namespace WoodHeart.Repository.Repositories.Ordering;

public class CartRepository(DataContext context)
    : Repository<Cart>(context), ICartRepository
{
    public async Task<Cart?> GetActiveForCustomerAsync(
        long customerId, CancellationToken cancellationToken = default) =>
        await WithLines()
            .FirstOrDefaultAsync(
                x => x.CustomerId == customerId && x.Status == CartStatus.Active,
                cancellationToken);

    public async Task<Cart?> GetActiveForGuestAsync(
        string anonymousTokenHash, CancellationToken cancellationToken = default) =>
        await WithLines()
            .FirstOrDefaultAsync(
                x => x.AnonymousToken == anonymousTokenHash && x.Status == CartStatus.Active,
                cancellationToken);

    public async Task<Cart?> GetWithLinesAsync(
        long cartId, CancellationToken cancellationToken = default) =>
        await WithLines().FirstOrDefaultAsync(x => x.Id == cartId, cancellationToken);

    /// <summary>
    /// A cart with everything pricing needs, in one round trip.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Tracked, not <c>AsNoTracking</c>: every caller of these methods is about
    /// to change the cart — add a line, change a quantity, set a zone — and an
    /// untracked graph would have to be re-attached line by line.
    /// </para>
    /// <para>
    /// <c>AsSplitQuery</c> because this is a collection include hanging off
    /// another include. As a single query PostgreSQL returns the cart and the
    /// variant columns repeated once per line; with a dozen lines and a long
    /// product description that is a lot of duplicated bytes over the wire for
    /// a page that renders on every visit.
    /// </para>
    /// <para>
    /// The product is included because the pricer needs its delivery surcharge
    /// and its status, and the name is what the cart page shows. Ordering by
    /// <c>Id</c> keeps the display stable: without it PostgreSQL is free to
    /// return the lines in any order, and a basket whose rows shuffle when you
    /// change a quantity looks broken.
    /// </para>
    /// </remarks>
    private IQueryable<Cart> WithLines() =>
        Set.Include(x => x.Lines.OrderBy(line => line.Id))
            .ThenInclude(line => line.ProductVariant)
            .ThenInclude(variant => variant.Product)
            // Filtered to the one image the basket actually shows. An
            // unfiltered include would drag every photograph of every product
            // in the cart across the wire to render a 64-pixel thumbnail.
            .ThenInclude(product => product.Media.Where(m => m.IsPrimary))
            .AsSplitQuery();
}
