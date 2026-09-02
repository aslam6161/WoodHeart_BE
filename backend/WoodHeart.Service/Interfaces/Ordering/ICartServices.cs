using WoodHeart.Domain.Pricing;
using WoodHeart.Repository;
using WoodHeart.Service.DTOs.Ordering;

namespace WoodHeart.Service.Interfaces.Ordering;

/// <summary>
/// The basket, for whoever is asking — signed in or not.
/// </summary>
/// <remarks>
/// <para>
/// No method takes a cart id. The cart is resolved from the caller's identity
/// on every call, because a basket a client can name by id is a basket a client
/// can name <i>someone else's</i> id for. Line ids are accepted, and every one
/// of them is checked against the resolved cart before it is touched.
/// </para>
/// </remarks>
public interface ICartService
{
    /// <summary>
    /// The caller's basket, priced. Returns an empty cart rather than a 404
    /// when there is none — "you have no basket" is not an error, and a
    /// storefront header that has to handle a 404 to show a zero is a
    /// storefront header that shows a spinner forever the first time something
    /// else goes wrong.
    /// </summary>
    Task<GeneralResponse<CartDto>> GetAsync(CancellationToken cancellationToken = default);

    Task<GeneralResponse<CartDto>> AddAsync(AddToCartDto dto, CancellationToken cancellationToken = default);

    /// <summary>Sets a line's quantity. Zero removes it.</summary>
    Task<GeneralResponse<CartDto>> UpdateLineAsync(
        long lineId, UpdateCartLineDto dto, CancellationToken cancellationToken = default);

    Task<GeneralResponse<CartDto>> RemoveLineAsync(
        long lineId, CancellationToken cancellationToken = default);

    Task<GeneralResponse<CartDto>> ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>Records the delivery zone, which is what makes delivery priceable.</summary>
    Task<GeneralResponse<CartDto>> SetDeliveryZoneAsync(
        SetDeliveryZoneDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Folds a guest's basket into the signed-in customer's on sign-in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called by the account flow, not by the client. <b>Quantities are summed
    /// rather than replaced</b>: someone who put a lamp in a basket on their
    /// phone, signed in on a laptop, and added the same lamp meant to have two
    /// — and a merge that silently discarded one of them would be a shop
    /// losing a sale it had already made.
    /// </para>
    /// </remarks>
    Task<GeneralResponse> MergeGuestCartAsync(
        string anonymousId, long customerId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Assembles the <see cref="PricingContext"/> from store settings.
/// </summary>
/// <remarks>
/// Separated from <see cref="ICartService"/> because order placement needs
/// exactly the same context, and the two must never disagree about the VAT rate
/// or the delivery charge. One place builds it; both use it.
/// </remarks>
public interface IPricingContextFactory
{
    Task<PricingContext> BuildAsync(
        Domain.Enums.Ordering.DeliveryZone? zone, CancellationToken cancellationToken = default);
}
