using System.Text.Json;
using Microsoft.Extensions.Logging;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.Helpers;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Ordering;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Jobs;
using WoodHeart.Service.Interfaces.Notifications;
using WoodHeart.Service.Services.Notifications;

namespace WoodHeart.Service.Services.Jobs;

/// <inheritdoc cref="IAbandonedCarts" />
/// <remarks>
/// <para>
/// <b>Two clocks, and they are not the same clock.</b> A basket is worth a
/// reminder within the evening and worth keeping for a month. The reminder runs
/// off how long the basket has been quiet; the sweep that closes one off runs
/// off the basket's own expiry, thirty days out. Collapsing them into one
/// number would mean either reminding somebody a month late or throwing their
/// basket away the same night.
/// </para>
/// <para>
/// <b>One transaction per basket</b>, for the reason every job here works that
/// way: a batch of forty where the thirty-first throws should still have
/// reminded thirty.
/// </para>
/// <para>
/// <b>The sweep never deletes.</b> A basket is the best record there is of what
/// somebody nearly bought — which lines, at what price, on what evening — and
/// tidying the rows away throws that away to save nothing.
/// </para>
/// </remarks>
public class AbandonedCarts(
    ICartRepository carts,
    INotificationQueue notifications,
    IStoreSettingService settings,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork,
    ILogger<AbandonedCarts> logger) : IAbandonedCarts
{
    /// <summary>Per run, for each half. An hour later the next run takes the next batch.</summary>
    private const int BatchSize = 100;

    /// <inheritdoc />
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var reminded = await RemindAsync(cancellationToken);

        // Run whatever happened above: closing off month-old baskets is not
        // conditional on the reminder being switched on.
        await SweepAsync(cancellationToken);

        return reminded;
    }

    // -------------------------------------------------------------------------
    // The reminder
    // -------------------------------------------------------------------------

    private async Task<int> RemindAsync(CancellationToken cancellationToken)
    {
        var hours = await settings.GetIntAsync(
            SettingKeys.CartRecoveryAfterHours, fallback: 6, cancellationToken);

        if (hours <= 0)
        {
            return 0;
        }

        var quiet = await carts.GetQuietForRecoveryAsync(
            clock.UtcNow.AddHours(-hours), BatchSize, cancellationToken);

        var reminded = 0;

        foreach (var cart in quiet)
        {
            try
            {
                await unitOfWork.ExecuteInTransactionAsync(
                    ct => RemindAboutAsync(cart, ct), cancellationToken);

                reminded++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Stamped inside the transaction, so a basket that failed here
                // is untouched and the next run sees it again.
                JobLog.CartReminderFailed(logger, exception, cart.Id);
            }
        }

        if (reminded > 0)
        {
            JobLog.CartsReminded(logger, reminded, hours);
        }

        return reminded;
    }

    private async Task<bool> RemindAboutAsync(Cart cart, CancellationToken cancellationToken)
    {
        var customer = cart.Customer;

        // The query asks for a customer, so this is a basket whose owner was
        // deleted between the read and now rather than a guest's.
        if (string.IsNullOrWhiteSpace(customer?.PhoneNumber))
        {
            cart.RecoveryNudgedAt = clock.UtcNow;
            carts.Update(cart);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return true;
        }

        // In the customer's own language, because the message is: a Bangla
        // reader being told about "Segun king bed" in the middle of a Bangla
        // sentence is the sort of half-translation that reads as carelessness.
        var lines = cart.Lines
            .Select(l => new
            {
                name = l.ProductVariant.Product.Name.For(customer.PreferredLanguage),
                quantity = l.Quantity
            })
            .ToArray();

        await notifications.EnqueueAsync(
            new NotificationRequest
            {
                Type = NotificationTemplates.CartAbandoned,

                // The basket, not the moment: this message is sent once ever,
                // and the key says so even if the stamp below were ever lost.
                IdempotencyKey = $"cart.abandoned:{cart.Id}",
                Payload = JsonSerializer.Serialize(new
                {
                    contactName = customer.FullName ?? string.Empty,
                    contactPhone = customer.PhoneNumber,
                    contactEmail = customer.Email,
                    language = customer.PreferredLanguage,
                    firstItem = lines[0].name,
                    otherItems = lines.Length - 1,
                    items = lines
                })
            },
            cancellationToken);

        // Stamped in the same unit of work as the message, so a basket can
        // never read "reminded" without one having been staged, or be reminded
        // twice because the stamp did not commit.
        cart.RecoveryNudgedAt = clock.UtcNow;
        carts.Update(cart);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }

    // -------------------------------------------------------------------------
    // The sweep
    // -------------------------------------------------------------------------

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        var expired = await carts.GetExpiredBeforeAsync(
            clock.UtcNow, BatchSize, cancellationToken);

        if (expired.Count == 0)
        {
            return;
        }

        foreach (var cart in expired)
        {
            cart.Status = CartStatus.Abandoned;
            carts.Update(cart);
        }

        // One save for the sweep. Unlike the reminder it sends nothing and
        // touches one column, so a failure costs a retry rather than a message.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        JobLog.CartsSweptUp(logger, expired.Count);
    }
}
