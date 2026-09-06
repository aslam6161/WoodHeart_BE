using WoodHeart.Repository;
using WoodHeart.Service.DTOs.Ordering;

namespace WoodHeart.Service.Interfaces.Ordering;

/// <summary>
/// Orders, from the customer's side.
/// </summary>
/// <remarks>
/// Admin order management is a separate service with separate rules — it can
/// move an order to any status the machine allows, and this one cannot.
/// Splitting them keeps "what may a customer do to their own order" a question
/// with one short answer.
/// </remarks>
public interface IOrderService
{
    /// <summary>The signed-in customer's own orders, newest first.</summary>
    Task<GeneralResponse<PagedResult<OrderSummaryDto>>> GetMineAsync(
        int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// One of the caller's own orders.
    /// </summary>
    /// <remarks>
    /// Resolved against the caller's identity, not by id alone. An order
    /// belonging to someone else comes back as not-found rather than forbidden,
    /// so the endpoint cannot be walked to discover which orders exist.
    /// </remarks>
    Task<GeneralResponse<OrderDetailDto>> GetMineByNumberAsync(
        string orderNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// A guest's order, by number and the phone number they gave.
    /// </summary>
    /// <remarks>
    /// Two facts rather than one. The order number alone is a series that can
    /// be walked; paired with the phone number on the order it is not worth
    /// walking. Rate limited for the same reason.
    /// </remarks>
    Task<GeneralResponse<OrderDetailDto>> LookupGuestOrderAsync(
        GuestOrderLookupDto dto, CancellationToken cancellationToken = default);

    /// <summary>Stops the customer's own order, while it is still theirs to stop.</summary>
    Task<GeneralResponse<OrderDetailDto>> CancelMineAsync(
        string orderNumber, CancelOrderDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attaches a guest's past orders to a new account.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called by the account flow on registration, matching on the normalised
    /// phone number. Someone who has bought twice as a guest and then signs up
    /// finds their history already there, which is most of what turns a guest
    /// into a repeat customer — and it costs almost nothing because the phone
    /// number was stored normalised from the start.
    /// </para>
    /// <para>
    /// Only orders with no customer are claimed. An order already attached to
    /// an account is never moved, whatever number it carries.
    /// </para>
    /// </remarks>
    Task<GeneralResponse> ClaimGuestOrdersAsync(
        long customerId, string phoneNumber, CancellationToken cancellationToken = default);
}
