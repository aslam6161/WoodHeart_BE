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
using WoodHeart.Service.Interfaces.Inventory;
using WoodHeart.Service.Interfaces.Ordering;
using WoodHeart.Service.Services.Notifications;
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
    IInventoryService inventory,
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
        var paymentBefore = order.PaymentStatus;

        order.Status = dto.Status;

        ApplyConsequences(order, dto.Status);

        // ApplyConsequences moves the payment status; the money it stands for
        // is written down here, where the clock and the actor are. A rider
        // handing cash over and a shop sending it back are the two commonest
        // things that happen to money in this shop, and neither of them goes
        // through the payment card.
        if (order.PaymentStatus != paymentBefore)
        {
            if (order.PaymentStatus == PaymentStatus.Paid)
            {
                AddPayment(
                    order, PaymentDirection.Received, order.AmountOutstanding, actor,
                    note: "Collected on delivery.");
            }
            else if (order.PaymentStatus == PaymentStatus.Refunded)
            {
                AddPayment(
                    order, PaymentDirection.Refunded, order.AmountPaid, actor,
                    note: $"Returned with order {order.OrderNumber}.");
            }
        }

        Record(order, from, dto.Status, actor, dto.Note?.Trim());

        // The shelf moves with the order: shipping books the sale, a
        // cancellation frees the hold, a return puts the units back. Staged
        // into the same save, so the status and the count cannot disagree.
        await inventory.ApplyStatusChangeAsync(order, from, dto.Status, actor, cancellationToken);

        orders.Update(order);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        OrderLog.StatusChanged(logger, order.OrderNumber, from.ToString(), dto.Status.ToString(), actor);

        if (Array.IndexOf(WorthTellingTheCustomer, dto.Status) >= 0)
        {
            await QueueStatusNotificationAsync(order, dto.Status, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        // A status change can move the money too: delivering a cash order
        // collects it, returning one sends it back. Delivery is excluded
        // because the message just queued already names the amount — two texts
        // for one event is two billed parts and one of them redundant. A
        // return has no message of its own, so the receipt is the only thing
        // that tells the customer their money is on its way.
        if (order.PaymentStatus != paymentBefore && dto.Status != OrderStatus.Delivered)
        {
            await QueuePaymentReceiptAsync(order, order.PaymentStatus, cancellationToken);
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

                // AdvancePaid as well as Unpaid. A made-to-order wardrobe is
                // taken with something down and the rest at the door, which is
                // the flow the shop uses for its largest sales — and until the
                // ledger made the balance visible, such an order quietly stayed
                // AdvancePaid for ever after the rider had been paid in full.
                if (order.PaymentStatus is PaymentStatus.Unpaid or PaymentStatus.AdvancePaid
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

        var amount = AmountFor(order, dto);

        if (amount is null)
        {
            return GeneralResponse<AdminOrderDetailDto>.Fail(
                OrderingErrors.PaymentAmountInvalid,
                dto.Status == PaymentStatus.Refunded || dto.Status == PaymentStatus.PartiallyRefunded
                    ? $"That is more than has been taken for this order ({order.AmountPaid})."
                    : $"That is more than is outstanding on this order ({order.AmountOutstanding}).");
        }

        order.PaymentStatus = dto.Status;

        if (amount.Amount > 0m)
        {
            AddPayment(
                order,
                dto.Status is PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded
                    ? PaymentDirection.Refunded
                    : PaymentDirection.Received,
                amount,
                actor,
                dto.Reference,
                dto.Note);
        }

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

        // Staged before the save, so the receipt and the claim that the money
        // arrived commit together. Most orders here are cash handed to a rider,
        // and this message is the only record either side has of that.
        await QueuePaymentReceiptAsync(order, dto.Status, cancellationToken);

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
                    language = order.CustomerLanguage,
                    grandTotal = order.GrandTotal.Amount,
                    currency = order.Currency,
                    paymentStatus = order.PaymentStatus.ToString(),

                    // So the delivered message can be the receipt for a cash
                    // order without becoming one for a prepaid one, where the
                    // money arrived days earlier.
                    paymentMethod = order.PaymentMethodCode
                })
            },
            cancellationToken);

    /// <summary>
    /// How much this entry is for, or null when the figure is impossible.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The common cases have one right answer and making somebody type it
    /// invites a typo: marking an order Paid means whatever is outstanding,
    /// and Refunded means everything taken so far. An advance is the case that
    /// has to be said, and the case the application could not previously hold.
    /// </para>
    /// <para>
    /// A figure larger than the order, or than was ever taken, is refused.
    /// <c>Unpaid</c> and <c>Failed</c> move no money at all and get a zero,
    /// which the caller writes no row for — a ledger of things that did not
    /// happen is not a ledger.
    /// </para>
    /// </remarks>
    private static Money? AmountFor(Order order, RecordPaymentDto dto)
    {
        var zero = Money.From(0m, order.Currency);

        var ceiling = dto.Status switch
        {
            PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded => order.AmountPaid,
            PaymentStatus.Unpaid or PaymentStatus.Failed => zero,
            _ => order.AmountOutstanding
        };

        if (ceiling.Amount <= 0m)
        {
            return zero;
        }

        if (dto.Amount is not { } given)
        {
            // An advance with no figure and none asked for cannot be guessed
            // at: the whole balance would be wrong by definition.
            return dto.Status == PaymentStatus.AdvancePaid
                ? order.RequiredAdvanceAmount is { } required && required.Amount <= ceiling.Amount
                    ? required
                    : null
                : ceiling;
        }

        return given > ceiling.Amount ? null : Money.From(given, order.Currency);
    }

    /// <summary>
    /// Writes one line of the ledger.
    /// </summary>
    /// <remarks>
    /// Append-only, like the timeline. <c>PaymentStatus</c> says where the
    /// money stands; this says what happened to get there, and it is the only
    /// one of the two that survives somebody pressing a button twice.
    /// </remarks>
    private void AddPayment(
        Order order,
        PaymentDirection direction,
        Money amount,
        string actor,
        string? reference = null,
        string? note = null) =>
        order.Payments.Add(new OrderPayment
        {
            OrderId = order.Id,
            Direction = direction,
            Amount = amount,
            MethodCode = order.PaymentMethodCode,
            Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
            ActorName = actor,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            OccurredAt = clock.UtcNow
        });

    /// <summary>
    /// The receipt for money in or money back.
    /// </summary>
    /// <remarks>
    /// Keyed on the order and the status it reached, so the delivery worker
    /// retrying cannot bill the shop twice at the gateway — and so an order
    /// that is paid and later refunded gets two messages rather than one
    /// swallowed as a duplicate.
    /// </remarks>
    private async Task QueuePaymentReceiptAsync(
        Order order, PaymentStatus status, CancellationToken cancellationToken) =>
        await notifications.EnqueueAsync(
            new NotificationRequest
            {
                Type = NotificationTemplates.PaymentStatusChanged,
                IdempotencyKey = $"payment.status:{order.OrderNumber}:{status}",
                Payload = JsonSerializer.Serialize(new
                {
                    orderNumber = order.OrderNumber,
                    status = status.ToString(),
                    contactName = order.ContactName,
                    contactPhone = order.ContactPhone,
                    contactEmail = order.ContactEmail,
                    language = order.CustomerLanguage,
                    grandTotal = order.GrandTotal.Amount,
                    currency = order.Currency,
                    paymentMethod = order.PaymentMethodCode,

                    // From the ledger, so an advance and a part refund can
                    // name real figures instead of talking around them.
                    amountPaid = order.AmountPaid.Amount,
                    amountOutstanding = order.AmountOutstanding.Amount
                })
            },
            cancellationToken);

    private static GeneralResponse<AdminOrderDetailDto> NotFound() =>
        GeneralResponse<AdminOrderDetailDto>.Fail(
            OrderingErrors.OrderNotFound, "We could not find that order.");
}
