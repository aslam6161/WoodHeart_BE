using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Ordering;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Inventory;
using WoodHeart.Service.Interfaces.Notifications;
using WoodHeart.Service.Services.Jobs;
using WoodHeart.Tests.Helper;

namespace WoodHeart.Tests.Jobs;

/// <summary>
/// The hold an abandoned gateway order has on the shelf, and when it ends.
/// </summary>
public class UnpaidOrderExpiryTests
{
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IInventoryService _inventory = Substitute.For<IInventoryService>();
    private readonly INotificationQueue _notifications = Substitute.For<INotificationQueue>();
    private readonly IStoreSettingService _settings = Substitute.For<IStoreSettingService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.Zero));

    private readonly UnpaidOrderExpiry _job;

    public UnpaidOrderExpiryTests()
    {
        _settings.GetIntAsync(SettingKeys.UnpaidOrderExpiryMinutes, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(60);

        _unitOfWork
            .ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<bool>>>()(CancellationToken.None));

        _job = new UnpaidOrderExpiry(
            _orders, _inventory, _notifications, _settings, _clock, _unitOfWork,
            NullLogger<UnpaidOrderExpiry>.Instance);
    }

    [Fact]
    public async Task Asks_for_orders_older_than_the_setting_and_nothing_younger()
    {
        _orders.GetUnpaidPendingBeforeAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);

        await _job.RunAsync();

        // Sixty minutes ago, exactly: an order placed 59 minutes ago is still
        // somebody typing a bKash PIN.
        await _orders.Received(1).GetUnpaidPendingBeforeAsync(
            _clock.UtcNow.AddMinutes(-60), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_stale_order_is_cancelled_its_hold_released_and_the_customer_told()
    {
        var order = Stale("WH-2609-00007");

        _orders.GetUnpaidPendingBeforeAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([order]);

        var cancelled = await _job.RunAsync();

        cancelled.ShouldBe(1);
        order.Status.ShouldBe(OrderStatus.Cancelled);

        // The timeline says the system did it and why, so the entry reads
        // correctly in six months.
        var entry = order.Timeline.ShouldHaveSingleItem();
        entry.FromStatus.ShouldBe(OrderStatus.Pending);
        entry.ToStatus.ShouldBe(OrderStatus.Cancelled);
        entry.ActorName.ShouldBe(UnpaidOrderExpiry.Actor);
        entry.ActorUserId.ShouldBeNull();
        entry.Note.ShouldBe("Not paid within 60 minutes.");

        // The shelf, through the same door a hand cancellation uses.
        await _inventory.Received(1).ApplyStatusChangeAsync(
            order, OrderStatus.Pending, OrderStatus.Cancelled, UnpaidOrderExpiry.Actor, Arg.Any<CancellationToken>());

        // The customer, idempotent on the order and status, so a hand
        // cancellation racing this one does not send two messages.
        await _notifications.Received(1).EnqueueAsync(
            Arg.Is<NotificationRequest>(r =>
                r.Type == "order.status_changed"
                && r.IdempotencyKey == "order.status:WH-2609-00007:Cancelled"
                && r.Payload.Contains("\"status\":\"Cancelled\"")),
            Arg.Any<CancellationToken>());

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task One_bad_order_does_not_stop_the_rest()
    {
        var first = Stale("WH-2609-00001");
        var second = Stale("WH-2609-00002");
        var third = Stale("WH-2609-00003");

        _orders.GetUnpaidPendingBeforeAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([first, second, third]);

        _inventory
            .When(i => i.ApplyStatusChangeAsync(second, Arg.Any<OrderStatus>(), Arg.Any<OrderStatus>(), Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("row version moved"));

        var cancelled = await _job.RunAsync();

        // Two of three, and the third was not skipped because the second
        // threw. The second is still Pending for the next run.
        cancelled.ShouldBe(2);
        first.Status.ShouldBe(OrderStatus.Cancelled);
        third.Status.ShouldBe(OrderStatus.Cancelled);
    }

    [Fact]
    public async Task Zero_minutes_switches_the_expiry_off()
    {
        _settings.GetIntAsync(SettingKeys.UnpaidOrderExpiryMinutes, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(0);

        var cancelled = await _job.RunAsync();

        cancelled.ShouldBe(0);
        await _orders.DidNotReceiveWithAnyArgs().GetUnpaidPendingBeforeAsync(default, default, default);
    }

    private Order Stale(string number) =>
        new()
        {
            Id = number.GetHashCode(),
            OrderNumber = number,
            ContactName = "Rakib Hasan",
            ContactPhone = "+8801712345678",
            ShippingAddress = DeliveryAddress.Create("Dhaka", "Dhaka", "House 12", area: "Dhanmondi"),
            DeliveryZone = DeliveryZone.InsideDhaka,
            Currency = Money.Bdt,
            Subtotal = Money.Taka(68_500m),
            DiscountTotal = Money.Zero(),
            GoodsNet = Money.Taka(63_720.93m),
            VatAmount = Money.Taka(4_779.07m),
            VatRatePercent = 7.5m,
            PricesIncludeVat = true,
            DeliveryFee = Money.Taka(1_500m),
            PaymentSurcharge = Money.Zero(),
            GrandTotal = Money.Taka(70_000m),
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Unpaid,
            PaymentMethodCode = "bkash",
            PlacedAt = _clock.UtcNow.AddHours(-2)
        };
}
