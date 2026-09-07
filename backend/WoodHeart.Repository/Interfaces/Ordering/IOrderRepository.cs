using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Enums.Ordering;

namespace WoodHeart.Repository.Interfaces.Ordering;

/// <summary>
/// Finds and loads orders. Stages changes; does not commit them.
/// </summary>
/// <remarks>
/// Reads that render an order to a person load its lines and its timeline;
/// reads that render a list load neither. That split is deliberate — a customer
/// with forty orders would otherwise pull four hundred lines to draw forty rows
/// of "3 items".
/// </remarks>
public interface IOrderRepository : IRepository<Order>
{
    /// <summary>One order with lines and timeline, by its human-facing number.</summary>
    Task<Order?> GetByNumberAsync(string orderNumber, CancellationToken cancellationToken = default);

    /// <summary>One order with lines and timeline, by id.</summary>
    Task<Order?> GetDetailAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// A customer's orders, newest first, with lines but no timeline.
    /// </summary>
    /// <remarks>
    /// Lines are included because the list shows thumbnails; the timeline is
    /// not, because nothing on a list page reads it.
    /// </remarks>
    Task<IReadOnlyList<Order>> GetForCustomerAsync(
        long customerId, int skip, int take, CancellationToken cancellationToken = default);

    Task<int> CountForCustomerAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The order already written for this idempotency key, if there is one.
    /// </summary>
    /// <remarks>
    /// The first half of the double-tap guard: a repeat of the same placement
    /// request returns the order it already made. The other half is the unique
    /// index, which catches the two requests that arrive close enough together
    /// for this lookup to miss.
    /// </remarks>
    Task<Order?> GetByIdempotencyKeyAsync(
        string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every order placed against a phone number, for claiming a guest's
    /// history when they register.
    /// </summary>
    Task<IReadOnlyList<Order>> GetUnclaimedForPhoneAsync(
        string contactPhone, CancellationToken cancellationToken = default);

    /// <summary>The admin list: filtered by status, newest first.</summary>
    Task<IReadOnlyList<Order>> SearchAsync(
        OrderStatus? status,
        string? term,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    Task<int> CountAsync(
        OrderStatus? status, string? term, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many orders sit in each status, in one round trip.
    /// </summary>
    /// <remarks>
    /// A GROUP BY rather than ten counts. The admin board draws a tab per
    /// status with a badge on it, and ten separate queries to render one row of
    /// tabs is the sort of thing that is invisible locally and obvious on a
    /// shared database.
    /// </remarks>
    Task<IReadOnlyDictionary<OrderStatus, int>> CountByStatusAsync(
        CancellationToken cancellationToken = default);
}
