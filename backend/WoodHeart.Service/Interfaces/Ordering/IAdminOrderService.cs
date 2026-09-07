using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Repository;
using WoodHeart.Service.DTOs.Ordering;

namespace WoodHeart.Service.Interfaces.Ordering;

/// <summary>
/// Orders, from behind the counter.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="IOrderService"/> rather than a set of extra methods
/// on it, because the two differ in the one way that matters: <b>nothing here
/// is scoped to the caller.</b> Every method reaches any order in the shop, and
/// the only thing standing between a request and somebody's delivery address is
/// the policy on the controller. Keeping that in its own type means the
/// authorization decision is made once, in one place, rather than method by
/// method on a service the storefront also calls.
/// </para>
/// <para>
/// What staff may do that a customer may not: move an order to any status
/// <c>OrderStatusMachine</c> allows, record where the money got to, correct the
/// delivery charge, and write notes the customer never sees.
/// </para>
/// </remarks>
public interface IAdminOrderService
{
    /// <summary>
    /// The order board: newest first, optionally filtered by status and a
    /// search over order number, phone and name.
    /// </summary>
    Task<GeneralResponse<PagedResult<AdminOrderSummaryDto>>> SearchAsync(
        OrderStatus? status,
        string? term,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>How many orders sit in each status, for the board's tabs.</summary>
    Task<GeneralResponse<IReadOnlyList<OrderStatusCountDto>>> GetStatusCountsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>One order in full, unmasked, with the moves that are legal from here.</summary>
    Task<GeneralResponse<AdminOrderDetailDto>> GetAsync(
        string orderNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the order along the work axis.
    /// </summary>
    /// <remarks>
    /// Refused when <c>OrderStatusMachine</c> does not allow the move. Some
    /// moves carry the other two axes with them — a delivered cash-on-delivery
    /// order is a paid one — and those are applied here rather than left to be
    /// remembered.
    /// </remarks>
    Task<GeneralResponse<AdminOrderDetailDto>> ChangeStatusAsync(
        string orderNumber, ChangeOrderStatusDto dto, CancellationToken cancellationToken = default);

    /// <summary>Records where the money got to, independently of the goods.</summary>
    Task<GeneralResponse<AdminOrderDetailDto>> RecordPaymentAsync(
        string orderNumber, RecordPaymentDto dto, CancellationToken cancellationToken = default);

    /// <summary>Records where the goods got to, for a part-shipped order.</summary>
    Task<GeneralResponse<AdminOrderDetailDto>> RecordFulfilmentAsync(
        string orderNumber, RecordFulfilmentDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Corrects the delivery charge, and the total with it.
    /// </summary>
    /// <remarks>
    /// Only while nothing has been collected and the goods have not left.
    /// Afterwards the order's total is what somebody was actually charged, and
    /// editing it would make the books disagree with the till.
    /// </remarks>
    Task<GeneralResponse<AdminOrderDetailDto>> OverrideDeliveryFeeAsync(
        string orderNumber, OverrideDeliveryFeeDto dto, CancellationToken cancellationToken = default);

    /// <summary>The staff notepad. Never shown to the customer.</summary>
    Task<GeneralResponse<AdminOrderDetailDto>> UpdateInternalNotesAsync(
        string orderNumber, UpdateInternalNotesDto dto, CancellationToken cancellationToken = default);
}
