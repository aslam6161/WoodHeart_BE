using WoodHeart.Domain.Entity.Ordering;

namespace WoodHeart.Repository.Interfaces.Ordering;

/// <summary>
/// Finds and loads carts. Stages changes; does not commit them.
/// </summary>
/// <remarks>
/// Every read here loads the lines and, through them, the variant and its
/// product. That is not laziness about projections — pricing a cart needs the
/// live unit price and the delivery surcharge of every line, so a cart without
/// them is never useful, and fetching them separately is the N+1 that shows up
/// on the busiest page of the site.
/// </remarks>
public interface ICartRepository : IRepository<Cart>
{
    /// <summary>The signed-in customer's active cart, with lines loaded.</summary>
    Task<Cart?> GetActiveForCustomerAsync(
        long customerId, CancellationToken cancellationToken = default);

    /// <summary>A guest's active cart, found by the hash of their token.</summary>
    Task<Cart?> GetActiveForGuestAsync(
        string anonymousTokenHash, CancellationToken cancellationToken = default);

    /// <summary>One cart by id, with lines loaded. Used after a create.</summary>
    Task<Cart?> GetWithLinesAsync(long cartId, CancellationToken cancellationToken = default);
}
