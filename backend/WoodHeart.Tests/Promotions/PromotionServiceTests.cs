using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Entity.Promotions;
using WoodHeart.Domain.Enums.Promotions;
using WoodHeart.Domain.Promotions;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository.Interfaces.Ordering;
using WoodHeart.Repository.Interfaces.Promotions;
using WoodHeart.Service.Interfaces.Promotions;
using WoodHeart.Service.Services.Promotions;
using WoodHeart.Tests.Helper;

namespace WoodHeart.Tests.Promotions;

/// <summary>
/// The database half of the engine: what it fetches, what it does not bother
/// fetching, and what it writes down afterwards.
/// </summary>
public class PromotionServiceTests
{
    private readonly IDiscountRepository _discounts = Substitute.For<IDiscountRepository>();
    private readonly IPromotionUsageRepository _usages = Substitute.For<IPromotionUsageRepository>();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly FakeClock _clock = new();

    private readonly PromotionService _service;

    public PromotionServiceTests()
    {
        _discounts.GetCandidatesAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns([]);

        _service = new PromotionService(
            _discounts, _usages, _orders, _clock, NullLogger<PromotionService>.Instance);
    }

    [Fact]
    public async Task An_empty_basket_with_no_codes_asks_the_database_nothing()
    {
        // This runs on every read of every basket, including the empty one in
        // the header of a first-time visitor's first page.
        var outcome = await _service.EvaluateAsync(Request([]));

        outcome.Applied.ShouldBeEmpty();

        await _discounts.DidNotReceiveWithAnyArgs().GetCandidatesAsync(default, default!, default);
    }

    [Fact]
    public async Task Whether_somebody_has_ordered_before_is_asked_only_when_it_matters()
    {
        _discounts.GetCandidatesAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns([Sale(firstOrderOnly: false)]);

        await _service.EvaluateAsync(Request([Line()], phone: "+8801712345678"));

        await _orders.DidNotReceiveWithAnyArgs().HasPlacedOrderAsync(default, default, default);
    }

    [Fact]
    public async Task A_returning_customer_does_not_get_the_new_customer_discount()
    {
        _discounts.GetCandidatesAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns([Sale(firstOrderOnly: true)]);

        _orders.HasPlacedOrderAsync(null, "+8801712345678", Arg.Any<CancellationToken>()).Returns(true);

        var outcome = await _service.EvaluateAsync(Request([Line()], phone: "+8801712345678"));

        outcome.Applied.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_guest_who_has_not_said_who_they_are_is_treated_as_new()
    {
        // The alternative is hiding a new-customer discount from every new
        // customer until they have typed a phone number at checkout.
        _discounts.GetCandidatesAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns([Sale(firstOrderOnly: true)]);

        var outcome = await _service.EvaluateAsync(Request([Line()]));

        outcome.Applied.ShouldHaveSingleItem();
        await _orders.DidNotReceiveWithAnyArgs().HasPlacedOrderAsync(default, default, default);
    }

    [Fact]
    public async Task A_redemption_is_recorded_against_the_order_and_the_phone_number()
    {
        var order = new Order
        {
            Id = 77,
            OrderNumber = "WH-2609-00077",
            CustomerId = null,
            ContactPhone = "+8801712345678"
        };

        PromotionUsage? written = null;

        await _usages.InsertAsync(
            Arg.Do<PromotionUsage>(usage => written = usage), Arg.Any<CancellationToken>());

        await _service.RecordUsageAsync(order, [Applied(Money.Taka(2_000m), code: "EID25")]);

        written.ShouldNotBeNull();
        written.DiscountId.ShouldBe(1);
        written.OrderId.ShouldBe(77);
        written.Code.ShouldBe("EID25");

        // A guest's identity for a per-customer limit. Without it "one per
        // customer" would mean nothing at all for most of the shop's buyers.
        written.CustomerId.ShouldBeNull();
        written.ContactPhone.ShouldBe("+8801712345678");
        written.Amount.ShouldBe(Money.Taka(2_000m));
        written.UsedAt.ShouldBe(_clock.UtcNow);
    }

    [Fact]
    public async Task A_discount_that_gave_nothing_does_not_burn_a_redemption()
    {
        // A free-shipping coupon on an order whose delivery staff had already
        // set by hand. Counting it would take one of the customer's two
        // allowed uses for nothing.
        var order = new Order { Id = 1, OrderNumber = "WH-2609-00001", ContactPhone = "+8801712345678" };

        await _service.RecordUsageAsync(order, [Applied(Money.Zero(), code: "FREEDEL")]);

        await _usages.DidNotReceiveWithAnyArgs().InsertAsync(default!, default);
    }

    // -------------------------------------------------------------------------
    // Fixtures
    // -------------------------------------------------------------------------

    private static PromotionRequest Request(
        IReadOnlyList<DiscountLine> lines,
        IReadOnlyCollection<string>? codes = null,
        long? customerId = null,
        string? phone = null) =>
        new(lines, Money.Zero(), null, null, codes ?? [], customerId, phone);

    private static DiscountLine Line() =>
        new(VariantId: 1, ProductId: 1, CategoryPath: "/1/", Quantity: 1, LineTotal: Money.Taka(50_000m));

    private static Discount Sale(bool firstOrderOnly) =>
        new()
        {
            Id = 1,
            Name = "Welcome",
            Type = DiscountType.Percentage,
            Value = 10m,
            Status = DiscountStatus.Active,
            FirstOrderOnly = firstOrderOnly
        };

    private static AppliedDiscount Applied(Money amount, string? code) =>
        new(1, "Welcome", code, DiscountType.Percentage, amount, new Dictionary<long, Money>());
}
