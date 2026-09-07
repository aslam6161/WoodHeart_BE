using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Entity.Payments;
using WoodHeart.Domain.Enums.Catalog;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.Enums.Payments;
using WoodHeart.Domain.Pricing;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Ordering;
using WoodHeart.Service.DTOs.Ordering;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Notifications;
using WoodHeart.Service.Interfaces.Ordering;
using WoodHeart.Service.Interfaces.Payments;
using WoodHeart.Service.Services.Ordering;
using WoodHeart.Service.Services.Payments;
using WoodHeart.Tests.Helper;

namespace WoodHeart.Tests.Ordering;

/// <summary>
/// Turning a basket into an order.
/// </summary>
/// <remarks>
/// <para>
/// The three behaviours worth the most attention, because each one costs real
/// money when it is wrong: <b>a repeated placement must not become a second
/// order</b>, <b>cash on delivery is Confirmed and Unpaid</b>, and <b>an
/// unavailable line stops the order rather than being quietly dropped</b>.
/// </para>
/// <para>
/// Everything about the bill is recomputed here from the caller's own cart, so
/// there is nothing a request could say that would change a price. That is
/// tested by the totals landing on the order matching what <c>CartPricer</c>
/// produces, rather than by asserting a request cannot carry a price — it
/// cannot, because the DTO has no field for one.
/// </para>
/// </remarks>
public class CheckoutServiceTests
{
    private const string AnonymousId = "guest-token";
    private const string OrderNumber = "WH-2608-00001";

    private readonly ICartRepository _carts = Substitute.For<ICartRepository>();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IPricingContextFactory _pricing = Substitute.For<IPricingContextFactory>();
    private readonly IPaymentProviderResolver _payments = Substitute.For<IPaymentProviderResolver>();
    private readonly INumberSequenceService _numbers = Substitute.For<INumberSequenceService>();
    private readonly INotificationQueue _notifications = Substitute.For<INotificationQueue>();
    private readonly IStoreSettingService _settings = Substitute.For<IStoreSettingService>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly ITokenHasher _hasher = Substitute.For<ITokenHasher>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeClock _clock = new();

    private readonly List<Order> _inserted = [];

    public CheckoutServiceTests()
    {
        _hasher.Hash(Arg.Any<string>()).Returns(call => $"hashed:{call.Arg<string>()}");
        _currentUser.AnonymousId.Returns(AnonymousId);
        _currentUser.Language.Returns("en");

        _pricing.BuildAsync(Arg.Any<DeliveryZone?>(), Arg.Any<Money?>(), Arg.Any<CancellationToken>())
            .Returns(new PricingContext(7.5m, PricesIncludeVat: true));

        _numbers.NextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(OrderNumber);

        _payments.ResolveAsync(
                Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<DeliveryZone>(), Arg.Any<CancellationToken>())
            .Returns(CashOnDelivery());

        _orders.InsertAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call => _inserted.Add(call.Arg<Order>()));

        // The real unit of work runs the delegate inside a transaction; the
        // substitute just runs it, so these tests exercise the body rather than
        // EF's transaction handling.
        _unitOfWork
            .ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<GeneralResponse<PlacedOrderDto>>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
                call.Arg<Func<CancellationToken, Task<GeneralResponse<PlacedOrderDto>>>>()(
                    CancellationToken.None));
    }

    private CheckoutService CreateService() =>
        new(_carts,
            _orders,
            _pricing,
            _payments,
            _numbers,
            _notifications,
            _settings,
            _currentUser,
            _hasher,
            _clock,
            _unitOfWork,
            NullLogger<CheckoutService>.Instance);

    // -------------------------------------------------------------------------
    // The happy path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_guest_can_place_an_order()
    {
        GivenABasket();

        var result = await CreateService().PlaceOrderAsync(Request(), idempotencyKey: null);

        result.IsSuccess.ShouldBeTrue(result.Message);
        result.Data!.OrderNumber.ShouldBe(OrderNumber);

        // No account, and that is the main path here rather than an edge case.
        _inserted.Single().CustomerId.ShouldBeNull();
    }

    [Fact]
    public async Task Cash_on_delivery_is_confirmed_and_unpaid()
    {
        GivenABasket();

        await CreateService().PlaceOrderAsync(Request(), idempotencyKey: null);

        var order = _inserted.Single();

        // Both halves matter. Confirmed, because the goods will ship — and
        // Unpaid, because no money has moved. A shop that marked this Paid
        // could not answer "how much cash is out with riders right now".
        order.Status.ShouldBe(OrderStatus.Confirmed);
        order.PaymentStatus.ShouldBe(PaymentStatus.Unpaid);
    }

    [Fact]
    public async Task The_basket_is_retired_rather_than_emptied()
    {
        var cart = GivenABasket();

        await CreateService().PlaceOrderAsync(Request(), idempotencyKey: null);

        cart.Status.ShouldBe(CartStatus.CheckedOut);

        // The lines survive. They are the evidence behind the order, and
        // "the customer says they ordered three chairs" needs something to
        // check against.
        cart.Lines.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task The_order_records_where_it_came_from()
    {
        var cart = GivenABasket();

        await CreateService().PlaceOrderAsync(Request(), idempotencyKey: null);

        _inserted.Single().CartId.ShouldBe(cart.Id);
    }

    [Fact]
    public async Task Placing_an_order_writes_the_first_two_timeline_entries()
    {
        GivenABasket();

        await CreateService().PlaceOrderAsync(Request(), idempotencyKey: null);

        var timeline = _inserted.Single().Timeline.ToList();

        // Placed, then confirmed. "Who moved this order and when" is a question
        // that gets asked, and it needs an answer from the first moment.
        timeline.Count.ShouldBe(2);
        timeline[0].ToStatus.ShouldBe(OrderStatus.Pending);
        timeline[0].FromStatus.ShouldBeNull();
        timeline[1].ToStatus.ShouldBe(OrderStatus.Confirmed);
    }

    [Fact]
    public async Task The_confirmation_is_staged_in_the_outbox()
    {
        GivenABasket();

        await CreateService().PlaceOrderAsync(Request(), idempotencyKey: null);

        // Staged, not sent. The SMS and the order it announces commit together,
        // so there is no window in which one exists without the other.
        await _notifications.Received(1).EnqueueAsync(
            Arg.Is<NotificationRequest>(r =>
                r.Type == "order.placed" && r.IdempotencyKey == $"order.placed:{OrderNumber}"),
            Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // The money, recomputed
    // -------------------------------------------------------------------------

    [Fact]
    public async Task The_totals_are_recomputed_from_the_basket()
    {
        GivenABasket(price: 10_000m, quantity: 2);

        await CreateService().PlaceOrderAsync(Request(), idempotencyKey: null);

        var order = _inserted.Single();

        order.Subtotal.Amount.ShouldBe(20_000m);
        order.GrandTotal.Amount.ShouldBe(20_000m);

        // 7.5% inclusive: 20,000 × 7.5 / 107.5 = 1,395.35, extracted rather
        // than added, so the customer still pays 20,000.
        order.VatAmount.Amount.ShouldBe(1395.35m);
        order.GoodsNet.Amount.ShouldBe(18_604.65m);
    }

    [Fact]
    public async Task The_VAT_rate_and_regime_are_copied_onto_the_order()
    {
        GivenABasket();

        await CreateService().PlaceOrderAsync(Request(), idempotencyKey: null);

        // Copied so an invoice reprinted in two years shows the rate that was
        // actually charged, not whatever the setting says by then.
        _inserted.Single().VatRatePercent.ShouldBe(7.5m);
        _inserted.Single().PricesIncludeVat.ShouldBeTrue();
    }

    [Fact]
    public async Task A_payment_surcharge_is_added_to_the_total_and_kept_separate()
    {
        GivenABasket(price: 10_000m);

        _payments.ResolveAsync(
                Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<DeliveryZone>(), Arg.Any<CancellationToken>())
            .Returns(CashOnDelivery(surcharge: 50m));

        await CreateService().PlaceOrderAsync(Request(), idempotencyKey: null);

        var order = _inserted.Single();

        order.PaymentSurcharge.Amount.ShouldBe(50m);
        order.GrandTotal.Amount.ShouldBe(10_050m);

        // Kept out of the delivery line: a customer who saw "delivery 500৳" on
        // the cart page and "delivery 550৳" on the invoice has been misled.
        order.DeliveryFee.Amount.ShouldBe(0m);
    }

    // -------------------------------------------------------------------------
    // The double tap
    // -------------------------------------------------------------------------

    [Fact]
    public async Task The_same_idempotency_key_returns_the_order_already_placed()
    {
        GivenABasket();

        _orders.GetByIdempotencyKeyAsync("key-1", Arg.Any<CancellationToken>())
            .Returns(new Order
            {
                Id = 7,
                OrderNumber = OrderNumber,
                Status = OrderStatus.Confirmed,
                GrandTotal = Money.Taka(10_000m)
            });

        var result = await CreateService().PlaceOrderAsync(Request(), "key-1");

        result.IsSuccess.ShouldBeTrue();
        result.Data!.OrderNumber.ShouldBe(OrderNumber);

        // The point of the flag: the storefront carries on to the confirmation
        // page exactly as it would have, and nobody gets two sofas.
        result.Data.AlreadyPlaced.ShouldBeTrue();
        _inserted.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_key_is_stored_so_the_index_can_catch_the_race_this_lookup_misses()
    {
        GivenABasket();

        await CreateService().PlaceOrderAsync(Request(), "key-1");

        _inserted.Single().IdempotencyKey.ShouldBe("key-1");
    }

    // -------------------------------------------------------------------------
    // Refusals
    // -------------------------------------------------------------------------

    [Fact]
    public async Task An_empty_basket_cannot_be_ordered()
    {
        _carts.GetActiveForGuestAsync($"hashed:{AnonymousId}", Arg.Any<CancellationToken>())
            .Returns((Cart?)null);

        var result = await CreateService().PlaceOrderAsync(Request(), idempotencyKey: null);

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(OrderingErrors.CartEmpty);
    }

    [Fact]
    public async Task A_withdrawn_product_stops_the_order_rather_than_being_dropped()
    {
        var cart = GivenABasket();
        cart.Lines.First().ProductVariant.Product.Status = ProductStatus.Archived;

        var result = await CreateService().PlaceOrderAsync(Request(), idempotencyKey: null);

        // The cart page prices around it and shows it greyed out; checkout
        // refuses. Silently dropping it would deliver two of the three things
        // somebody paid for, and they would find out at the door.
        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(OrderingErrors.LineNotPurchasable);
        _inserted.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_payment_method_that_is_not_available_stops_the_order()
    {
        GivenABasket();

        _payments.ResolveAsync(
                Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<DeliveryZone>(), Arg.Any<CancellationToken>())
            .Returns((EligiblePaymentMethod?)null);

        var result = await CreateService().PlaceOrderAsync(Request(), idempotencyKey: null);

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(PaymentErrors.MethodNotAvailable);
    }

    [Fact]
    public async Task An_unreadable_phone_number_stops_the_order()
    {
        GivenABasket();

        var result = await CreateService()
            .PlaceOrderAsync(Request(phone: "12345"), idempotencyKey: null);

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(OrderingErrors.ContactPhoneInvalid);
    }

    [Fact]
    public async Task The_phone_number_is_normalised_onto_the_order()
    {
        GivenABasket();

        await CreateService()
            .PlaceOrderAsync(Request(phone: "017-1234-5678"), idempotencyKey: null);

        // Stored in one spelling, because it is the key that links a guest
        // order to an account created later. Four spellings would mean four
        // customers.
        _inserted.Single().ContactPhone.ShouldBe("+8801712345678");
    }

    // -------------------------------------------------------------------------
    // The zone comes from the address
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_Sylhet_address_is_priced_outside_Dhaka_whatever_the_cart_said()
    {
        var cart = GivenABasket();

        // The customer picked Inside Dhaka in the cart and then typed a Sylhet
        // address. Charging the city rate here is a loss on exactly the
        // deliveries that travel furthest.
        cart.DeliveryZone = DeliveryZone.InsideDhaka;

        await CreateService()
            .PlaceOrderAsync(Request(district: "Sylhet"), idempotencyKey: null);

        _inserted.Single().DeliveryZone.ShouldBe(DeliveryZone.OutsideDhaka);
    }

    // -------------------------------------------------------------------------
    // Snapshotting
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Every_line_freezes_what_it_displays()
    {
        GivenABasket(price: 4500m, quantity: 2);

        await CreateService().PlaceOrderAsync(Request(), idempotencyKey: null);

        var line = _inserted.Single().Lines.Single();

        // Copied, not joined. A rename next month must not rewrite what this
        // invoice says was sold.
        line.ProductNameEn.ShouldBe("Product 1");
        line.ProductSlug.ShouldBe("product-1");
        line.Sku.ShouldBe("WH-SKU-1");
        line.VariantName.ShouldBe("Segun · 6ft");
        line.UnitPrice.Amount.ShouldBe(4500m);
        line.Quantity.ShouldBe(2);
        line.LineTotal.Amount.ShouldBe(9000m);
    }

    // -------------------------------------------------------------------------
    // Builders
    // -------------------------------------------------------------------------

    private Cart GivenABasket(decimal price = 10_000m, int quantity = 1)
    {
        var variant = new ProductVariant
        {
            Id = 1,
            Sku = "WH-SKU-1",
            VariantName = "Segun · 6ft",
            IsActive = true,
            PriceOverride = Money.Taka(price),
            ProductId = 1,
            Product = new Product
            {
                Id = 1,
                Code = "WH-1",
                Name = LocalizedText.Create("Product 1"),
                Slug = Slug.From("product-1"),
                BasePrice = Money.Taka(price),
                Status = ProductStatus.Active,
                ProductType = ProductType.Stocked
            }
        };

        var cart = new Cart
        {
            Id = 5,
            Currency = Money.Bdt,
            Status = CartStatus.Active,
            AnonymousToken = $"hashed:{AnonymousId}",
            Lines =
            [
                new CartLine
                {
                    Id = 1,
                    CartId = 5,
                    ProductVariantId = variant.Id,
                    ProductVariant = variant,
                    Quantity = quantity,
                    UnitPriceAtAdd = variant.EffectivePrice
                }
            ]
        };

        _carts.GetActiveForGuestAsync($"hashed:{AnonymousId}", Arg.Any<CancellationToken>())
            .Returns(cart);

        return cart;
    }

    private static PlaceOrderDto Request(
        string phone = "01712345678", string district = "Dhaka") =>
        new()
        {
            ContactName = "Rakib Hasan",
            ContactPhone = phone,
            PaymentMethodCode = PaymentMethodCodes.CashOnDelivery,
            ShippingAddress = new DeliveryAddressDto
            {
                Division = "Dhaka",
                District = district,
                Area = "Dhanmondi",
                AddressLine = "House 12, Road 3",
                Landmark = "Opposite the mosque"
            }
        };

    private static EligiblePaymentMethod CashOnDelivery(decimal surcharge = 0m) =>
        new(
            new PaymentMethodConfig
            {
                Code = PaymentMethodCodes.CashOnDelivery,
                DisplayName = LocalizedText.Create("Cash on delivery"),
                IsEnabled = true,
                Mode = PaymentMode.Live
            },
            new CodPaymentProvider(),
            Money.Taka(surcharge));
}
