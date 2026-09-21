using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Helpers;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Inventory;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Jobs;
using WoodHeart.Service.Interfaces.Notifications;
using WoodHeart.Service.Services.Notifications;

namespace WoodHeart.Service.Services.Jobs;

/// <inheritdoc cref="ILowStockDigest" />
/// <remarks>
/// <para>
/// The same rows the admin's "low" filter shows, so the message and the
/// screen never disagree about what is low: the variant's own reorder level,
/// or the store threshold where none is set, and never-stocked variants
/// included because they are sold out on the storefront.
/// </para>
/// <para>
/// The message goes to <c>store.phone</c> and <c>store.email</c>. Those are
/// the shop's particulars from the settings screen, so the owner chooses
/// where the morning message lands without a deployment.
/// </para>
/// </remarks>
public class LowStockDigest(
    IStockRepository stock,
    INotificationQueue notifications,
    IStoreSettingService settings,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork,
    ILogger<LowStockDigest> logger) : ILowStockDigest
{
    /// <summary>Bangladesh keeps one offset all year, so a fixed +06:00 is exact.</summary>
    public static readonly TimeSpan DhakaOffset = TimeSpan.FromHours(6);

    /// <summary>
    /// The most lines an email carries. A shop with more than fifty lines low
    /// has a stock-take to do, not a longer email to read.
    /// </summary>
    private const int MaxLines = 50;

    /// <inheritdoc />
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        if (!await settings.GetBoolAsync(SettingKeys.LowStockDigest, fallback: true, cancellationToken))
        {
            return 0;
        }

        var threshold = await settings.GetIntAsync(SettingKeys.LowStockThreshold, fallback: 5, cancellationToken);
        var total = await stock.CountAsync(term: null, lowOnly: true, threshold, cancellationToken);

        if (total == 0)
        {
            return 0;
        }

        var phone = (await settings.GetStringAsync(SettingKeys.StorePhone, cancellationToken))?.Trim();
        var email = (await settings.GetStringAsync(SettingKeys.StoreEmail, cancellationToken))?.Trim();

        if (string.IsNullOrEmpty(phone) && string.IsNullOrEmpty(email))
        {
            JobLog.DigestNoRecipient(logger, total);
            return 0;
        }

        var rows = await stock.SearchAsync(term: null, lowOnly: true, threshold, skip: 0, MaxLines, cancellationToken);

        // The shop's date, not the server's: the job fires at nine in Dhaka,
        // which is three in the morning in UTC and, on the wrong side of
        // midnight, yesterday.
        var date = clock.UtcNow.ToOffset(DhakaOffset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        await notifications.EnqueueAsync(
            new NotificationRequest
            {
                Type = NotificationTemplates.StockLow,
                IdempotencyKey = $"stock.low:{date}",
                Payload = JsonSerializer.Serialize(new
                {
                    date,
                    total,
                    lines = rows.Select(row => new
                    {
                        product = row.Variant.Product?.Name.En ?? string.Empty,
                        variant = row.Variant.VariantName,
                        sku = row.Variant.Sku,
                        stocked = row.Item is not null,
                        available = row.Item?.Available ?? 0,
                        reorderLevel = row.Item?.ReorderLevel ?? threshold
                    }),
                    recipientPhone = phone ?? string.Empty,
                    recipientEmail = email ?? string.Empty
                })
            },
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        JobLog.DigestQueued(logger, date, total, phone ?? email!);

        return total;
    }
}
