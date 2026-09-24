using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.Entity.Identity;
using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Ordering;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Notifications;
using WoodHeart.Service.Services.Jobs;
using WoodHeart.Service.Services.Notifications;
using WoodHeart.Tests.Helper;

namespace WoodHeart.Tests.Jobs;

/// <summary>
/// The job that reminds somebody their basket is still here, and closes off the
/// ones nobody came back to.
/// </summary>
/// <remarks>
/// <para>
/// <b>Once per basket, ever.</b> A second reminder is nagging and a second
/// message the shop pays for. The stamp is written in the same unit of work as
/// the message, so a basket cannot read "reminded" without one having been
/// staged, nor be reminded twice because the stamp did not commit.
/// </para>
/// <para>
/// <b>Two clocks.</b> The reminder runs off how long a basket has been quiet;
/// the sweep runs off the basket's own thirty-day expiry. A basket is worth a
/// reminder within the evening and worth keeping for a month.
/// </para>
/// </remarks>
public class AbandonedCartsTests
{
    private readonly ICartRepository _carts = Substitute.For<ICartRepository>();
    private readonly INotificationQueue _notifications = Substitute.For<INotificationQueue>();
    private readonly IStoreSettingService _settings = Substitute.For<IStoreSettingService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 9, 24, 18, 0, 0, TimeSpan.Zero));

    private readonly AbandonedCarts _job;

    public AbandonedCartsTests()
    {
        _settings.GetIntAsync(
                SettingKeys.CartRecoveryAfterHours, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(6);

        _carts.GetQuietForRecoveryAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);

        _carts.GetExpiredBeforeAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);

        _unitOfWork
            .ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                call.Arg<Func<CancellationToken, Task<bool>>>()(CancellationToken.None));

        _job = new AbandonedCarts(
            _carts, _notifications, _settings, _clock, _unitOfWork,
            NullLogger<AbandonedCarts>.Instance);
    }

    // -------------------------------------------------------------------------
    // The reminder
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_quiet_basket_earns_its_owner_one_message()
    {
        var cart = Quiet();

        Quietly(cart);

        var reminded = await _job.RunAsync();

        reminded.ShouldBe(1);

        var request = Queued();

        request.ShouldNotBeNull();
        request.Type.ShouldBe(NotificationTemplates.CartAbandoned);
        request.Payload.ShouldContain("Segun king bed");
    }

    [Fact]
    public async Task And_the_basket_remembers_it_so_nobody_is_told_twice()
    {
        var cart = Quiet();

        Quietly(cart);

        await _job.RunAsync();

        cart.RecoveryNudgedAt.ShouldBe(_clock.UtcNow);
    }

    [Fact]
    public async Task The_message_is_keyed_to_the_basket_rather_than_the_moment()
    {
        // Once ever, not once a run. Keying on the clock would send a second
        // message the moment the stamp were ever lost or reset.
        var cart = Quiet();

        Quietly(cart);

        await _job.RunAsync();

        Queued()!.IdempotencyKey.ShouldBe($"cart.abandoned:{cart.Id}");
    }

    [Fact]
    public async Task Nothing_is_sent_when_the_shop_has_switched_the_reminder_off()
    {
        _settings.GetIntAsync(
                SettingKeys.CartRecoveryAfterHours, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(0);

        Quietly(Quiet());

        var reminded = await _job.RunAsync();

        reminded.ShouldBe(0);
        Queued().ShouldBeNull();

        // Zero switches off the reminder, not the housekeeping.
        await _carts.Received(1).GetExpiredBeforeAsync(
            Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_window_asked_for_is_the_one_the_shop_configured()
    {
        _settings.GetIntAsync(
                SettingKeys.CartRecoveryAfterHours, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(12);

        await _job.RunAsync();

        await _carts.Received(1).GetQuietForRecoveryAsync(
            _clock.UtcNow.AddHours(-12), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_basket_whose_owner_vanished_is_stamped_rather_than_retried_forever()
    {
        // The query asks for a basket with a customer, so this is one whose
        // owner went between the read and the write. Stamping it stops the job
        // picking it up again every hour for a month.
        var cart = Quiet();

        cart.Customer = null;

        Quietly(cart);

        await _job.RunAsync();

        Queued().ShouldBeNull();
        cart.RecoveryNudgedAt.ShouldBe(_clock.UtcNow);
    }

    [Fact]
    public async Task One_basket_going_wrong_does_not_cost_the_others_their_message()
    {
        var bad = Quiet(id: 1);
        var good = Quiet(id: 2);

        bad.Lines.Clear();

        Quietly(bad, good);

        var reminded = await _job.RunAsync();

        // The empty one throws on its first line; the next one is still sent.
        reminded.ShouldBe(1);
        good.RecoveryNudgedAt.ShouldBe(_clock.UtcNow);
        bad.RecoveryNudgedAt.ShouldBeNull();
    }

    [Fact]
    public async Task The_message_is_written_in_the_customers_own_language()
    {
        var cart = Quiet();

        cart.Customer!.PreferredLanguage = "bn";
        cart.Lines.First().ProductVariant.Product.Name =
            LocalizedText.Create("Segun king bed", "সেগুন কিং বেড");

        Quietly(cart);

        await _job.RunAsync();

        var payload = Queued()!.Payload;

        payload.ShouldContain("\"language\":\"bn\"");
        payload.ShouldNotContain("Segun king bed");
    }

    // -------------------------------------------------------------------------
    // The sweep
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_basket_past_its_thirty_days_is_closed_off_and_kept()
    {
        var stale = Quiet(id: 9);

        _carts.GetExpiredBeforeAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([stale]);

        await _job.RunAsync();

        stale.Status.ShouldBe(CartStatus.Abandoned);

        // Closed off, not deleted: it is the best record there is of what
        // somebody nearly bought.
        _carts.Received().Update(stale);
        _carts.DidNotReceive().Delete(Arg.Any<Cart>());
    }

    [Fact]
    public async Task And_the_sweep_says_nothing_to_anybody()
    {
        _carts.GetExpiredBeforeAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Quiet(id: 9)]);

        await _job.RunAsync();

        // A month-old basket is not an occasion for a text message.
        Queued().ShouldBeNull();
    }

    // -------------------------------------------------------------------------

    private void Quietly(params Cart[] found) =>
        _carts.GetQuietForRecoveryAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(found);

    private NotificationRequest? Queued() =>
        _notifications.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(INotificationQueue.EnqueueAsync))
            .Select(c => (NotificationRequest)c.GetArguments()[0]!)
            .FirstOrDefault();

    private static Cart Quiet(long id = 1) =>
        new()
        {
            Id = id,
            CustomerId = 5,
            Status = CartStatus.Active,
            Currency = Money.Bdt,
            CreatedAt = new DateTimeOffset(2026, 9, 24, 6, 0, 0, TimeSpan.Zero),
            ExpiresAt = new DateTimeOffset(2026, 10, 24, 6, 0, 0, TimeSpan.Zero),
            Customer = new AppUser
            {
                Id = 5,
                FullName = "Ayesha Siddiqua",
                PhoneNumber = "+8801712349999",
                Email = "ayesha@example.com",
                PreferredLanguage = "en"
            },
            Lines =
            [
                new CartLine
                {
                    ProductVariantId = 42,
                    Quantity = 1,
                    UnitPriceAtAdd = Money.Taka(68_500m),
                    ProductVariant = new ProductVariant
                    {
                        Id = 42,
                        Product = new Product { Name = LocalizedText.Create("Segun king bed") }
                    }
                }
            ]
        };
}
