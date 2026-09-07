using WoodHeart.Repository;
using WoodHeart.Service.DTOs.Ordering;

namespace WoodHeart.Service.Interfaces.Ordering;

/// <summary>
/// Turns the caller's basket into an order.
/// </summary>
/// <remarks>
/// <para>
/// One path for guests and members alike. Most furniture in this market is
/// bought without an account, so the signed-out case is the main one — the only
/// difference is whether <c>CustomerId</c> ends up set.
/// </para>
/// <para>
/// <b>Nothing about money crosses this boundary inwards.</b> The request
/// carries a contact, an address and a payment method; every figure is
/// recomputed from the caller's own cart through the same
/// <c>CartPricer</c> the cart page used.
/// </para>
/// </remarks>
public interface ICheckoutService
{
    /// <summary>
    /// The payment methods this basket may use, priced.
    /// </summary>
    /// <remarks>
    /// Takes the address's zone rather than the cart's chosen one, because the
    /// method a shop offers can depend on how far the delivery goes — cash on
    /// delivery to Bandarban is a decision, not a default.
    /// </remarks>
    Task<GeneralResponse<IReadOnlyList<PaymentMethodDto>>> GetPaymentMethodsAsync(
        DeliveryAddressDto? address, CancellationToken cancellationToken = default);

    /// <summary>
    /// Places the order.
    /// </summary>
    /// <param name="idempotencyKey">
    /// From the <c>Idempotency-Key</c> header. When the same key arrives twice,
    /// the order made the first time is returned rather than a second one.
    /// </param>
    Task<GeneralResponse<PlacedOrderDto>> PlaceOrderAsync(
        PlaceOrderDto dto,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);
}
