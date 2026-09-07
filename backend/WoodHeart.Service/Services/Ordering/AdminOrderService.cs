using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Identity;
using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.Helpers;
using WoodHeart.Domain.Ordering;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Ordering;
using WoodHeart.Service.DTOs.Ordering;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Notifications;
using WoodHeart.Service.Interfaces.Ordering;
using WoodHeart.Service.Mapping.Ordering;

namespace WoodHeart.Service.Services.Ordering;

/// <summary>
/// Orders, from behind the counter.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here is scoped to the caller</b>, which is the whole reason it is
/// a separate service from <see cref="OrderService"/>. Every method reaches any
/// order in the shop; the policy on <c>AdminOrdersController</c> is what stands
/// between a request and somebody's delivery address, and it is easier to be
/// sure of that when the two audiences do not share a class.
/// </para>
/// <para>
/// <b>Every change writes a timeline entry.</b> Not as decoration: "who
/// cancelled this order", "who took the delivery charge down to 1,600৳" and
/// "who marked it paid" are questions that get asked months later, and the
/// append-only timeline is the only thing that can answer them. A change that
/// skipped it would be a change nobody could account for.
/// </para>
/// </remarks>
public class AdminOrderService(
    IOrderRepository orders,
    UserManager<AppUser> users,
    INotificationQueue notifications,
    ICurrentUserService currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork,
    ILogger<AdminOrderService> logger) : IAdminOrderService
{
    /// <summary>
    /// Statuses the customer is told about.
    /// </summary>
    /// <remarks>
    /// Not every move is news. "Processing" and "ReadyToShip" are the shop
    /// talking to itself, and an SMS for each one costs money and trains the
    /// customer to ignore the ones that matter.
    /// </remarks>
    private static readonly OrderStatus[] WorthTellingTheCustomer =
        [OrderStatus.Confirmed, OrderStatus.Shipped, OrderStatus.Delivered, OrderStatus.Cancelled];

    // -------------------------------------------------------------------------
    // Reads
    // -------------------------------------------------------------------------

    public async Task<GeneralResponse<PagedResult<AdminOrderSummaryDto>>> SearchAsync(
        OrderStatus? status,
        string? term,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, OrderRules.MaxPageSize);

        var total = await orders.CountAsync(status, term, cancellationToken);

        var rows = await orders.SearchAsync(
            status, term, (page - 1) * pageSize, pageSize, cancellationToken);

        return GeneralResponse<PagedResult<AdminOrderSummaryDto>>.Success(
            new PagedResult<AdminOrderSummaryDto>
            {
                Items = [.. rows.Select(AdminOrderMapper.ToSummary)],
                Total = total,
                Page = page,
                PageSize = pageSize
            });
    }

    public async Task<GeneralResponse<IReadOnlyList<OrderStatusCountDto>>> GetStatusCountsAsync(
        CancellationToken cancellationToken = default)
    {
        var counts = await orders.CountByStatusAsync(cancellationToken);

        // Every status, including the empty ones. A tab that vanishes when it
        // reaches zero and reappears later is a worse board than one with a
        // quiet zero on it.
        return GeneralResponse<IReadOnlyList<OrderStatusCountDto>>.Success(
        [
            .. Enum.GetValues<OrderStatus>()
                .Select(status => new OrderStatusCountDto
                {
                    Status = status,
                    Count = counts.TryGetValue(status, out var count) ? count : 0
                })
        ]);
    }

    public async Task<GeneralResponse<AdminOrderDetailDto>> GetAsync(
        string orderNumber, CancellationToken cancellationToken = default)
    {
        var order = await FindAsync(orderNumber, cancellationToken);

        return order is null
            ? NotFound()
            : GeneralResponse<AdminOrderDetailDto>.Success(AdminOrderMapper.ToDetail(order));
    }

    // -------------------------------------------------------------------------
    // The work axis
    // -------------------------------------------------------------------------

    public async Task<GeneralResponse<AdminOrderDetailDto>> ChangeStatusAsync(
        string orderNumber, ChangeOrderStatusDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var order = await FindAsync(orderNumber, cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        if (order.Status == dto.Status)
        {
            // Not an error. Two operators with the same order open, and the
            // second press should be a no-op rather than a red banner.
            return GeneralResponse<AdminOrderDetailDto>.Success(AdminOrderMapper.ToDetail(order));
        }

        if (!OrderStatusMachine.CanTransition(order.Status, dto.Status))
        {
            return GeneralResponse<AdminOrderDetailDto>.Fail(
                OrderingErrors.OrderTransitionInvalid,
                $"An order cannot go from {order.Status} to {dto.Status}.");
        }

        // A cancellation with no reason is the entry somebody will be asked
        // about in six months. The other moves are ordinary progress.
        if (dto.Status == OrderStatus.Cancelled && string.IsNullOrWhiteSpace(dto.Note))
        {
            return GeneralResponse<AdminOrderDetailDto>.Fail(
                OrderingErrors.ReasonRequired, "Please say why the order is being cancelled.");
        }

        var actor = await ActorNameAsync();
        var from = order.Status;

        order.Status = dto.Status;

        ApplyConsequences(order, dto.Status);
        Record(order, from, dto.Status, actor, dto.Note?.Trim());

        orders.Update(order);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        OrderLog.StatusChanged(logger, order.OrderNumber, from.ToString(), dto.Status.ToString(), actor);

        if (Array.IndexOf(WorthTellingTheCustomer, dto.Status) >= 0)
        {
            await QueueStatusNotificationAsync(order, dto.Status, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return GeneralResponse<AdminOrderDetailDto>.Success(AdminOrderMapper.ToDetail(order));
    }

    /// <summary>
    /// The other two axes, where the work axis settles them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Delivered marks a cash-on-delivery order paid.</b> On this payment
    /// method, delivery <i>is</i> the collection — a refused doorstep is a
    /// <see cref="OrderStatus.Returned"/>, never a Delivered. Leaving it Unpaid
    /// would mean every completed COD order in the shop reads as money
    /// outstanding, and the revenue figures would be wrong by roughly the whole
    /// business. Staff can still set the payment status by hand where the cash
    /// did not actually arrive.
    /// </para>
    /// <para>
    /// Nothing here touches an order paid through a gateway: its money arrived
    /// at its own moment and this is not it.
    /// </para>
    /// </remarks>
    private static void ApplyConsequences(Order order, OrderStatus to)
    {
        switch (to)
        {
            case OrderStatus.Delivered:
                order.FulfilmentStatus = FulfilmentStatus.Fulfilled;

                if (order.PaymentStatus == PaymentStatus.Unpaid
                    && string.Equals(
                        order.PaymentMethodCode,
                        PaymentMethodCodes.CashOnDelivery,
                        StringComparison.OrdinalIgnoreCase))
                {
                    order.PaymentStatus = PaymentStatus.Paid;
                }

                break;

            case OrderStatus.Returned:
                order.FulfilmentStatus = FulfilmentStatus.Returned;
                break;

            // Nothing shipped, so nothing to unship. The fulfilment status is
            // left where it was rather than reset, because a cancelled order
            // that had already gone out part-way is a real situation and
            // erasing that is how the warehouse loses a chair.
            case OrderStatus.Cancelled:
                break;

            // Refunded follows Returned, and the money went back with it.
            case OrderStatus.Refunded:
                if (PaymentStatusMachine.CanTransition(order.PaymentStatus, PaymentStatus.Refunded))
                {
                    order.PaymentStatus = PaymentStatus.Refunded;
                }

                break;

            default:
                break;
        }
    }

    // -------------------------------------------------------------------------
    // The money axis
    // -------------------------------------------------------------------------

    public async Task<GeneralResponse<AdminOrderDetailDto>> RecordPaymentAsync(
        string orderNumber, RecordPaymentDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var order = await FindAsync(orderNumber, cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        if (order.PaymentStatus == dto.Status)
        {
            return GeneralResponse<AdminOrderDetailDto>.Success(AdminOrderMapper.ToDetail(order));
        }

        if (!PaymentStatusMachine.CanTransition(order.PaymentStatus, dto.Status))
        {
            return GeneralResponse<AdminOrderDetailDto>.Fail(
                OrderingErrors.PaymentTransitionInvalid,
                $"Payment cannot go from {order.PaymentStatus} to {dto.Status}.");
        }

        var actor = await ActorNameAsync();
        var from = order.PaymentStatus;

        order.PaymentStatus = dto.Status;

        // Same status on both ends: the order has not moved as a piece of work,
        // only the money under it. The timeline is the only append-only record
        // there is, and a payment nobody can attribute is worse than an
        // untidy history.
        Record(
            order,
            order.Status,
            order.Status,
            actor,
            Describe($"Payment {from} → {dto.Status}", dto.Note));

        orders.Update(order);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        OrderLog.PaymentRecorded(logger, order.OrderNumber, from.ToString(), dto.Status.ToString(), actor);

        return GeneralResponse<AdminOrderDetailDto>.Success(AdminOrderMapper.ToDetail(order));
    }

    public async Task<GeneralResponse<AdminOrderDetailDto>> RecordFulfilmentAsync(
        string orderNumber, RecordFulfilmentDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var order = await FindAsync(orderNumber, cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        if (order.FulfilmentStatus == dto.Status)
        {
            return GeneralResponse<AdminOrderDetailDto>.Success(AdminOrderMapper.ToDetail(order));
        }

        var actor = await ActorNameAsync();
        var from = order.FulfilmentStatus;

        // No machine on this axis, deliberately. A part-shipped order goes
        // backwards and forwards as vans come and go — a bed today, the
        // wardrobe in three weeks, one of them returned — and a graph that
        // tried to describe that would be wrong before it was finished.
        order.FulfilmentStatus = dto.Status;

        Record(
            order,
            order.Status,
            order.Status,
            actor,
            Describe($"Fulfilment {from} → {dto.Status}", dto.Note));

        orders.Update(order);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return GeneralResponse<AdminOrderDetailDto>.Success(AdminOrderMapper.ToDetail(order));
    }

    // -------------------------------------------------------------------------
    // The delivery charge
    // -------------------------------------------------------------------------

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// This is the point of the whole per-product delivery model. Summing each
    /// product's charge is right when the items are separate jobs and wrong
    /// when they are not: a bed and its two bedside tables price as three
    /// deliveries and go out on one van with the same two men. No rate card can
    /// express that; the person looking at the order can see it in a second.
    /// </para>
    /// <para>
    /// Only the delivery line moves. VAT is charged on the goods, so the net,
    /// the VAT and the surcharge are untouched and the total is rebuilt from
    /// all four — which keeps
    /// <c>goodsNet + vat + delivery + surcharge == grandTotal</c> exactly true,
    /// the same invariant checkout established.
    /// </para>
    /// </remarks>
    public async Task<GeneralResponse<AdminOrderDetailDto>> OverrideDeliveryFeeAsync(
        string orderNumber, OverrideDeliveryFeeDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var order = await FindAsync(orderNumber, cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(dto.Reason))
        {
            return GeneralResponse<AdminOrderDetailDto>.Fail(
                OrderingErrors.ReasonRequired,
                "Please say why the delivery charge is being changed.");
        }

        if (!AdminOrderMapper.CanEditDeliveryFee(order))
        {
            return GeneralResponse<AdminOrderDetailDto>.Fail(
                OrderingErrors.OrderAmountLocked,
                "This order's total can no longer be changed. Take a payment or issue a refund instead.");
        }

        if (dto.DeliveryFee < 0m)
        {
            return GeneralResponse<AdminOrderDetailDto>.Fail(
                OrderingErrors.OrderAmountLocked, "A delivery charge cannot be negative.");
        }

        var was = order.DeliveryFee;
        var now = Money.From(dto.DeliveryFee, order.Currency);

        if (was.Amount == now.Amount)
        {
            return GeneralResponse<AdminOrderDetailDto>.Success(AdminOrderMapper.ToDetail(order));
        }

        var actor = await ActorNameAsync();

        order.DeliveryFee = now;
        order.DeliveryOverridden = true;

        // A hand-set figure is not a waived one, even when it is zero. "Free
        // because the basket passed the threshold" and "free because we decided
        // to" are different facts, and only one of them is a rule.
        order.DeliveryWaived = false;

        order.GrandTotal = order.GoodsNet
            .Add(order.VatAmount)
            .Add(now)
            .Add(order.PaymentSurcharge);

        Record(
            order,
            order.Status,
            order.Status,
            actor,
            Describe(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Delivery charge {was.Amount:0.##} → {now.Amount:0.##} {order.Currency}"),
                dto.Reason));

        orders.Update(order);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        OrderLog.DeliveryFeeOverridden(
            logger, order.OrderNumber, was.Amount, now.Amount, actor);

        return GeneralResponse<AdminOrderDetailDto>.Success(AdminOrderMapper.ToDetail(order));
    }

    // -------------------------------------------------------------------------
    // Notes
    // -------------------------------------------------------------------------

    public async Task<GeneralResponse<AdminOrderDetailDto>> UpdateInternalNotesAsync(
        string orderNumber, UpdateInternalNotesDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var order = await FindAsync(orderNumber, cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        // No timeline entry. A notepad that wrote an audit row every time
        // somebody fixed a typo would bury the entries that matter, and unlike
        // the totals this changes nothing a customer is charged.
        order.InternalNotes = string.IsNullOrWhiteSpace(dto.InternalNotes)
            ? null
            : dto.InternalNotes.Trim();

        orders.Update(order);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return GeneralResponse<AdminOrderDetailDto>.Success(AdminOrderMapper.ToDetail(order));
    }

    // -------------------------------------------------------------------------
    // Shared helpers
    // -------------------------------------------------------------------------

    private async Task<Order?> FindAsync(string? orderNumber, CancellationToken cancellationToken) =>
        await orders.GetByNumberAsync(orderNumber?.Trim() ?? string.Empty, cancellationToken);

    private void Record(
        Order order, OrderStatus? from, OrderStatus to, string actor, string? note) =>
        order.Timeline.Add(new OrderTimelineEntry
        {
            OrderId = order.Id,
            FromStatus = from,
            ToStatus = to,
            ActorUserId = currentUser.UserId,
            ActorName = actor,
            Note = note,
            OccurredAt = clock.UtcNow
        });

    /// <summary>
    /// Who did it, in a form that still reads a year later.
    /// </summary>
    /// <remarks>
    /// The name is copied onto the timeline row rather than joined from the
    /// user, so a member of staff who leaves and is deactivated does not erase
    /// their name from the record of what they did. Falls back through the
    /// login handle to a bare "Staff" — an unattributed entry is worth more than
    /// no entry.
    /// </remarks>
    private async Task<string> ActorNameAsync()
    {
        if (currentUser.UserId is not { } userId)
        {
            return "System";
        }

        var user = await users.FindByIdAsync(userId.ToString(CultureInfo.InvariantCulture));

        return user?.FullName is { Length: > 0 } name
            ? name
            : user?.UserName is { Length: > 0 } handle
                ? handle
                : "Staff";
    }

    private static string Describe(string what, string? note) =>
        string.IsNullOrWhiteSpace(note) ? what : $"{what} — {note.Trim()}";

    /// <summary>
    /// Stages the customer's SMS in the same unit of work as the change.
    /// </summary>
    /// <remarks>
    /// Keyed on the order number and the status it reached, so the worker
    /// retrying cannot bill the shop twice at the gateway — and so an order that
    /// legitimately reaches two different statuses gets two messages.
    /// </remarks>
    private async Task QueueStatusNotificationAsync(
        Order order, OrderStatus status, CancellationToken cancellationToken) =>
        await notifications.EnqueueAsync(
            new NotificationRequest
            {
                Type = "order.status_changed",
                IdempotencyKey = $"order.status:{order.OrderNumber}:{status}",
                Payload = JsonSerializer.Serialize(new
                {
                    orderNumber = order.OrderNumber,
                    status = status.ToString(),
                    contactName = order.ContactName,
                    contactPhone = order.ContactPhone,
                    contactEmail = order.ContactEmail,
                    grandTotal = order.GrandTotal.Amount,
                    currency = order.Currency,
                    paymentStatus = order.PaymentStatus.ToString()
                })
            },
            cancellationToken);

    private static GeneralResponse<AdminOrderDetailDto> NotFound() =>
        GeneralResponse<AdminOrderDetailDto>.Fail(
            OrderingErrors.OrderNotFound, "We could not find that order.");
}
