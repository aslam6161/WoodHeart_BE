using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.Entity.Inventory;
using WoodHeart.Domain.Enums.Catalog;
using WoodHeart.Domain.Enums.Inventory;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Inventory;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Notifications;
using WoodHeart.Service.Services.Jobs;
using WoodHeart.Service.Services.Notifications;
using WoodHeart.Tests.Helper;

namespace WoodHeart.Tests.Jobs;

/// <summary>
/// The morning message: when it goes, to whom, and what it says.
/// </summary>
public class LowStockDigestTests
{
    private readonly IStockRepository _stock = Substitute.For<IStockRepository>();
    private readonly INotificationQueue _notifications = Substitute.For<INotificationQueue>();
    private readonly IStoreSettingService _settings = Substitute.For<IStoreSettingService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    // 02:30 UTC on the 22nd is 08:30 on the 22nd in Dhaka; the date in the
    // key has to be the shop's, not the server's.
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 9, 21, 20, 30, 0, TimeSpan.Zero));

    private readonly LowStockDigest _job;

    public LowStockDigestTests()
    {
        _settings.GetBoolAsync(SettingKeys.LowStockDigest, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _settings.GetIntAsync(SettingKeys.LowStockThreshold, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(5);
        _settings.GetStringAsync(SettingKeys.StorePhone, Arg.Any<CancellationToken>()).Returns("+8801712345678");
        _settings.GetStringAsync(SettingKeys.StoreEmail, Arg.Any<CancellationToken>()).Returns("shop@woodheart.com.bd");

        _job = new LowStockDigest(
            _stock, _notifications, _settings, _clock, _unitOfWork, NullLogger<LowStockDigest>.Instance);
    }

    [Fact]
    public async Task Nothing_low_means_nothing_sent()
    {
        _stock.CountAsync(null, true, 5, Arg.Any<CancellationToken>()).Returns(0);

        var sent = await _job.RunAsync();

        sent.ShouldBe(0);
        await _notifications.DidNotReceiveWithAnyArgs().EnqueueAsync(default!, default);
    }

    [Fact]
    public async Task The_message_lists_the_low_lines_and_is_keyed_to_the_shop_date()
    {
        _stock.CountAsync(null, true, 5, Arg.Any<CancellationToken>()).Returns(2);
        _stock.SearchAsync(null, true, Arg.Is(5), Arg.Is(0), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Low("Segun King Bed", "Segun · 6ft", "WH-BED-001-SG-6", onHand: 2), Unstocked("Dining Chair", "Segun", "WH-CHR-001")]);

        NotificationRequest? request = null;
        await _notifications.EnqueueAsync(Arg.Do<NotificationRequest>(r => request = r), Arg.Any<CancellationToken>());

        var sent = await _job.RunAsync();

        sent.ShouldBe(2);
        request.ShouldNotBeNull();
        request.Type.ShouldBe(NotificationTemplates.StockLow);

        // Dhaka's date, not UTC's, so a restart at 03:10 UTC does not send a
        // second message for what Dhaka still calls the same morning.
        request.IdempotencyKey.ShouldBe("stock.low:2026-09-22");

        using var payload = JsonDocument.Parse(request.Payload);
        var root = payload.RootElement;

        root.GetProperty("total").GetInt32().ShouldBe(2);
        root.GetProperty("recipientPhone").GetString().ShouldBe("+8801712345678");
        root.GetProperty("recipientEmail").GetString().ShouldBe("shop@woodheart.com.bd");

        var lines = root.GetProperty("lines").EnumerateArray().ToList();
        lines[0].GetProperty("product").GetString().ShouldBe("Segun King Bed");
        lines[0].GetProperty("available").GetInt32().ShouldBe(2);
        lines[0].GetProperty("stocked").GetBoolean().ShouldBeTrue();
        lines[1].GetProperty("stocked").GetBoolean().ShouldBeFalse();

        // Queued into the caller's unit of work, then saved here — the job
        // is the unit of work.
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Nobody_to_tell_means_a_warning_not_a_crash()
    {
        _settings.GetStringAsync(SettingKeys.StorePhone, Arg.Any<CancellationToken>()).Returns("");
        _settings.GetStringAsync(SettingKeys.StoreEmail, Arg.Any<CancellationToken>()).Returns((string?)null);
        _stock.CountAsync(null, true, 5, Arg.Any<CancellationToken>()).Returns(3);

        var sent = await _job.RunAsync();

        sent.ShouldBe(0);
        await _notifications.DidNotReceiveWithAnyArgs().EnqueueAsync(default!, default);
    }

    [Fact]
    public async Task Switched_off_in_settings_means_switched_off()
    {
        _settings.GetBoolAsync(SettingKeys.LowStockDigest, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(false);

        await _job.RunAsync();

        await _stock.DidNotReceiveWithAnyArgs().CountAsync(default, default, default, default);
    }

    [Fact]
    public void The_template_names_the_first_three_and_counts_the_rest()
    {
        var payload = JsonSerializer.Serialize(new
        {
            date = "2026-09-22",
            total = 5,
            lines = new object[]
            {
                new { product = "Segun King Bed", variant = "Segun · 6ft", sku = "A", stocked = true, available = 2 },
                new { product = "Three-Seater Sofa", variant = "Olive", sku = "B", stocked = true, available = 0 },
                new { product = "Dining Chair", variant = "Segun", sku = "C", stocked = false, available = 0 },
                new { product = "Coffee Table", variant = "Walnut", sku = "D", stocked = true, available = 1 },
                new { product = "Bookshelf", variant = "Natural", sku = "E", stocked = true, available = 3 }
            },
            recipientPhone = "+8801712345678",
            recipientEmail = "shop@woodheart.com.bd"
        });

        var rendered = NotificationTemplates.Render(NotificationTemplates.StockLow, payload, "01712345678")!.Value;

        rendered.SmsText.ShouldBe(
            "WoodHeart stock: 5 lines low - Segun King Bed (Segun · 6ft) 2 left, "
            + "Three-Seater Sofa (Olive) sold out, Dining Chair (Segun) never stocked and 2 more. See Stock in the admin.");
        rendered.RecipientPhone.ShouldBe("+8801712345678");
        rendered.RecipientEmail.ShouldBe("shop@woodheart.com.bd");
        rendered.EmailSubject.ShouldBe("Low stock: 5 lines — 2026-09-22");

        // The email carries all five, the SMS only three.
        rendered.EmailHtml!.ShouldContain("Bookshelf");
        rendered.EmailHtml!.ShouldContain("never stocked");
    }

    private static StockLevel Low(string product, string variant, string sku, int onHand)
    {
        var level = Unstocked(product, variant, sku);
        var item = new StockItem { ProductVariantId = level.Variant.Id, ReorderLevel = null };

        item.Apply(StockMovementType.Purchase, onHand, "Seed", DateTimeOffset.UnixEpoch);

        return level with { Item = item };
    }

    private static StockLevel Unstocked(string product, string variant, string sku) =>
        new(
            new ProductVariant
            {
                Id = sku.GetHashCode(),
                Sku = sku,
                VariantName = variant,
                Product = new Product
                {
                    Name = LocalizedText.Create(product),
                    Slug = Slug.From(product.ToLowerInvariant().Replace(' ', '-')),
                    Code = sku,
                    BasePrice = Money.Taka(1_000m),
                    ProductType = ProductType.Stocked
                }
            },
            null);
}
