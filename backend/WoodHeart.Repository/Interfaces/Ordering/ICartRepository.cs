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

    /// <summary>
    /// Baskets that have gone quiet and whose owner can be reached.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only baskets with a customer on them. A guest leaves an anonymous token
    /// and nothing else, so there is no one to write to — filtered here rather
    /// than in the job, because it is the database that can answer it without
    /// dragging every guest basket in the shop across the wire.
    /// </para>
    /// <para>
    /// Only baskets with something in them, and only ones nobody has reminded
    /// yet. The customer is loaded because the message needs their number and
    /// the language to write it in.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<Cart>> GetQuietForRecoveryAsync(
        DateTimeOffset idleSince, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Baskets past their own expiry, for the sweep that closes them off.
    /// </summary>
    /// <remarks>
    /// The rows survive the sweep. A basket is the best record there is of what
    /// somebody nearly bought, and deleting it to tidy up throws that away.
    /// </remarks>
    Task<IReadOnlyList<Cart>> GetExpiredBeforeAsync(
        DateTimeOffset before, int take, CancellationToken cancellationToken = default);
}
