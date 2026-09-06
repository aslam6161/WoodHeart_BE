using Microsoft.Extensions.Logging;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.Helpers;
using WoodHeart.Domain.Ordering;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Ordering;
using WoodHeart.Service.DTOs.Ordering;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Ordering;
using WoodHeart.Service.Mapping.Ordering;

namespace WoodHeart.Service.Services.Ordering;

/// <summary>
/// Orders as their owner sees them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every read is scoped to the caller.</b> A signed-in customer's orders are
/// found by their user id; a guest's by the pair of order number and the phone
/// number on the order. Nothing here fetches an order by id alone, so there is
/// no request shape that reads a stranger's delivery address.
/// </para>
/// <para>
/// An order that exists but belongs to somebody else comes back as not-found,
/// never as forbidden. A 403 confirms the order number is real, which is enough
/// to map the shop's whole order series by walking it.
/// </para>
/// </remarks>
public class OrderService(
    IOrderRepository orders,
    ICurrentUserService currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork,
    ILogger<OrderService> logger) : IOrderService
{
    public async Task<GeneralResponse<PagedResult<OrderSummaryDto>>> GetMineAsync(
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } customerId)
        {
            return GeneralResponse<PagedResult<OrderSummaryDto>>.Fail(
                IdentityErrors.NotAuthenticated, "Please sign in to see your orders.");
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, OrderRules.MaxPageSize);

        var total = await orders.CountForCustomerAsync(customerId, cancellationToken);

        var rows = await orders.GetForCustomerAsync(
            customerId, (page - 1) * pageSize, pageSize, cancellationToken);

        return GeneralResponse<PagedResult<OrderSummaryDto>>.Success(new PagedResult<OrderSummaryDto>
        {
            Items = [.. rows.Select(order => OrderMapper.ToSummary(order, currentUser.Language))],
            Total = total,
            Page = page,
            PageSize = pageSize
        });
    }

    public async Task<GeneralResponse<OrderDetailDto>> GetMineByNumberAsync(
        string orderNumber, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } customerId)
        {
            return GeneralResponse<OrderDetailDto>.Fail(
                IdentityErrors.NotAuthenticated, "Please sign in to see your orders.");
        }

        var order = await orders.GetByNumberAsync(orderNumber?.Trim() ?? string.Empty, cancellationToken);

        return order is null || order.CustomerId != customerId
            ? NotFound()
            : GeneralResponse<OrderDetailDto>.Success(
                OrderMapper.ToDetail(order, currentUser.Language));
    }

    public async Task<GeneralResponse<OrderDetailDto>> LookupGuestOrderAsync(
        GuestOrderLookupDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        // Normalised before comparing, because the number typed into this box
        // will not be spelled the way it was typed at checkout. Without it the
        // feature works for whoever writes +880 both times and for nobody else.
        if (!PhoneNumber.TryParse(dto.ContactPhone, out var phone) || phone is null)
        {
            return NotFound();
        }

        var order = await orders.GetByNumberAsync(dto.OrderNumber?.Trim() ?? string.Empty, cancellationToken);

        // Both must match, and a mismatch of either is the same answer. Saying
        // "that order exists but the number is wrong" would turn this into a
        // way to confirm which order numbers are real.
        return order is null
               || !string.Equals(order.ContactPhone, phone.Value, StringComparison.Ordinal)
            ? NotFound()
            : GeneralResponse<OrderDetailDto>.Success(
                OrderMapper.ToDetail(order, currentUser.Language));
    }

    public async Task<GeneralResponse<OrderDetailDto>> CancelMineAsync(
        string orderNumber, CancelOrderDto dto, CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } customerId)
        {
            return GeneralResponse<OrderDetailDto>.Fail(
                IdentityErrors.NotAuthenticated, "Please sign in to manage your orders.");
        }

        var order = await orders.GetByNumberAsync(orderNumber?.Trim() ?? string.Empty, cancellationToken);

        if (order is null || order.CustomerId != customerId)
        {
            return NotFound();
        }

        // Narrower than what staff may cancel, and deliberately so. Once the
        // shop has started building a made-to-order wardrobe, stopping it is a
        // conversation about a deposit rather than a button.
        if (!OrderStatusMachine.IsCustomerCancellable(order.Status))
        {
            return GeneralResponse<OrderDetailDto>.Fail(
                OrderingErrors.OrderNotCancellable,
                "This order has gone too far to cancel online. Please call us.");
        }

        if (!OrderStatusMachine.CanTransition(order.Status, OrderStatus.Cancelled))
        {
            return GeneralResponse<OrderDetailDto>.Fail(
                OrderingErrors.OrderTransitionInvalid, "This order cannot be cancelled.");
        }

        var from = order.Status;

        order.Timeline.Add(new OrderTimelineEntry
        {
            OrderId = order.Id,
            FromStatus = from,
            ToStatus = OrderStatus.Cancelled,
            ActorUserId = customerId,
            ActorName = "Customer",
            Note = string.IsNullOrWhiteSpace(dto?.Reason) ? "Cancelled by the customer." : dto.Reason.Trim(),
            OccurredAt = clock.UtcNow
        });

        order.Status = OrderStatus.Cancelled;

        orders.Update(order);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        OrderLog.StatusChanged(
            logger, order.OrderNumber, from.ToString(), OrderStatus.Cancelled.ToString(), "Customer");

        return GeneralResponse<OrderDetailDto>.Success(
            OrderMapper.ToDetail(order, currentUser.Language));
    }

    public async Task<GeneralResponse> ClaimGuestOrdersAsync(
        long customerId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (!PhoneNumber.TryParse(phoneNumber, out var phone) || phone is null)
        {
            return GeneralResponse.Success();
        }

        var unclaimed = await orders.GetUnclaimedForPhoneAsync(phone.Value, cancellationToken);

        if (unclaimed.Count == 0)
        {
            return GeneralResponse.Success();
        }

        foreach (var order in unclaimed)
        {
            order.CustomerId = customerId;
        }

        orders.UpdateRange(unclaimed);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        OrderLog.GuestOrdersClaimed(logger, unclaimed.Count, customerId);

        return GeneralResponse.Success();
    }

    private static GeneralResponse<OrderDetailDto> NotFound() =>
        GeneralResponse<OrderDetailDto>.Fail(
            OrderingErrors.OrderNotFound, "We could not find that order.");
}
