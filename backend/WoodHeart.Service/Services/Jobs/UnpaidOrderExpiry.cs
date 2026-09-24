using System.Text.Json;
using Microsoft.Extensions.Logging;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Helpers;
using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Ordering;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Inventory;
using WoodHeart.Service.Interfaces.Jobs;
using WoodHeart.Service.Interfaces.Notifications;
using WoodHeart.Service.Services.Notifications;

namespace WoodHeart.Service.Services.Jobs;

/// <inheritdoc cref="IUnpaidOrderExpiry" />
/// <remarks>
/// <para>
/// <b>One transaction per order.</b> A batch of forty stale orders where the
/// thirty-first throws should still have cancelled thirty, and the next run
/// should pick up the ten. All-or-nothing across the batch would give the
/// shop a job that fails every ten minutes over one bad row and releases
/// nothing.
/// </para>
/// <para>
/// The customer is told, with the same message a hand cancellation sends:
/// "please call if this is unexpected". Somebody whose bKash app crashed
/// mid-payment needs to know the order did not go through, and the message
/// is how they find out before the bed is sold to somebody else.
/// </para>
/// </remarks>
public class UnpaidOrderExpiry(
    IOrderRepository orders,
    IInventoryService inventory,
    INotificationQueue notifications,
    IStoreSettingService settings,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork,
    ILogger<UnpaidOrderExpiry> logger) : IUnpaidOrderExpiry
{
    public const string Actor = "System";

    /// <summary>Per run. Ten minutes later the next run takes the next hundred.</summary>
    private const int BatchSize = 100;

    /// <inheritdoc />
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var minutes = await settings.GetIntAsync(
            SettingKeys.UnpaidOrderExpiryMinutes, fallback: 60, cancellationToken);

        if (minutes <= 0)
        {
            return 0;
        }

        var cutoff = clock.UtcNow.AddMinutes(-minutes);
        var stale = await orders.GetUnpaidPendingBeforeAsync(cutoff, BatchSize, cancellationToken);
        var cancelled = 0;

        foreach (var order in stale)
        {
            try
            {
                await unitOfWork.ExecuteInTransactionAsync(
                    ct => CancelAsync(order, minutes, ct), cancellationToken);

                cancelled++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The next run sees this order again; the log says why it is
                // still there.
                JobLog.ExpiryFailed(logger, exception, order.OrderNumber);
            }
        }

        if (cancelled > 0)
        {
            JobLog.Expired(logger, cancelled, minutes);
        }

        return cancelled;
    }

    private async Task<bool> CancelAsync(Order order, int minutes, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        order.Status = OrderStatus.Cancelled;
        order.Timeline.Add(new OrderTimelineEntry
        {
            OrderId = order.Id,
            FromStatus = OrderStatus.Pending,
            ToStatus = OrderStatus.Cancelled,
            ActorName = Actor,
            Note = $"Not paid within {minutes} minutes.",
            OccurredAt = now
        });

        // Frees the hold. Staged into the same save as the status, so the
        // order cannot read Cancelled while the bed is still off the shelf.
        await inventory.ApplyStatusChangeAsync(
            order, OrderStatus.Pending, OrderStatus.Cancelled, Actor, cancellationToken);

        await notifications.EnqueueAsync(
            new NotificationRequest
            {
                Type = NotificationTemplates.OrderStatusChanged,
                IdempotencyKey = $"order.status:{order.OrderNumber}:{OrderStatus.Cancelled}",
                Payload = JsonSerializer.Serialize(new
                {
                    orderNumber = order.OrderNumber,
                    status = OrderStatus.Cancelled.ToString(),
                    contactName = order.ContactName,
                    contactPhone = order.ContactPhone,
                    contactEmail = order.ContactEmail,
                    language = order.CustomerLanguage,
                    grandTotal = order.GrandTotal.Amount,
                    currency = order.Currency,
                    paymentStatus = order.PaymentStatus.ToString()
                })
            },
            cancellationToken);

        orders.Update(order);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}

internal static partial class JobLog
{
    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Information,
        Message = "Cancelled {Count} order(s) left unpaid for more than {Minutes} minutes; their stock is released.")]
    public static partial void Expired(ILogger logger, int count, int minutes);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Error,
        Message = "Could not expire unpaid order {OrderNumber}; it will be tried again next run.")]
    public static partial void ExpiryFailed(ILogger logger, Exception exception, string orderNumber);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Information,
        Message = "Low-stock digest for {Date}: {Count} line(s) queued to {Recipient}.")]
    public static partial void DigestQueued(ILogger logger, string date, int count, string recipient);

    [LoggerMessage(
        EventId = 2004,
        Level = LogLevel.Information,
        Message = "Reminded {Count} customer(s) about a basket left untouched for more than {Hours} hour(s).")]
    public static partial void CartsReminded(ILogger logger, int count, int hours);

    [LoggerMessage(
        EventId = 2005,
        Level = LogLevel.Error,
        Message = "Could not remind anyone about basket {CartId}; it will be tried again next run.")]
    public static partial void CartReminderFailed(ILogger logger, Exception exception, long cartId);

    [LoggerMessage(
        EventId = 2006,
        Level = LogLevel.Information,
        Message = "Closed off {Count} basket(s) past their thirty days. The rows are kept.")]
    public static partial void CartsSweptUp(ILogger logger, int count);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Warning,
        Message = "Low-stock digest: {Count} line(s) low but neither store.phone nor store.email is set, so nobody was told.")]
    public static partial void DigestNoRecipient(ILogger logger, int count);
}
