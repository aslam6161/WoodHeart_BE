using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Entity.Payments;
using WoodHeart.Domain.Entity.Quotations;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.Enums.Quotations;
using WoodHeart.Domain.Pricing;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Catalog;
using WoodHeart.Repository.Interfaces.Ordering;
using WoodHeart.Repository.Interfaces.Quotations;
using WoodHeart.Service.DTOs.Ordering;
using WoodHeart.Service.DTOs.Quotations;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Inventory;
using WoodHeart.Service.Interfaces.Notifications;
using WoodHeart.Service.Interfaces.Ordering;
using WoodHeart.Service.Interfaces.Payments;
using WoodHeart.Service.Services.Quotations;
using WoodHeart.Tests.Helper;

namespace WoodHeart.Tests.Quotations;

/// <summary>
/// Writing a quotation, sending it, the customer answering it, and the order
/// it becomes.
/// </summary>
/// <remarks>
/// The one that matters most is the last: converting must honour the figure
/// the customer agreed to, down to the VAT rate. Everything else is a guard
/// around that promise.
/// </remarks>
public class QuotationServiceTests
{
    private const string Number = "WHQ-2609-00001";
    private const long VariantId = 42;

    private readonly IQuotationRepository _quotations = Substitute.For<IQuotationRepository>();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IProductVariantRepository _variants = Substitute.For<IProductVariantRepository>();
    private readonly IPricingContextFactory _pricing = Substitute.For<IPricingContextFactory>();
    private readonly IPaymentProviderResolver _payments = Substitute.For<IPaymentProviderResolver>();
    private readonly INumberSequenceService _numbers = Substitute.For<INumberSequenceService>();
    private readonly INotificationQueue _notifications = Substitute.For<INotificationQueue>();
    private readonly IInventoryService _inventory = Substitute.For<IInventoryService>();
    private readonly IStoreSettingService _settings = Substitute.For<IStoreSettingService>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 9, 20, 6, 0, 0, TimeSpan.Zero));

    private readonly List<Quotation> _inserted = [];
    private readonly List<Order> _placed = [];
    private readonly QuotationService _service;

    public QuotationServiceTests()
    {
        _currentUser.Language.Returns("en");

        _numbers.NextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Number);

        // 7.5%, added on top, no delivery charged unless a zone is known.
        _pricing.BuildAsync(
                Arg.Any<DeliveryZone?>(), Arg.Any<Money?>(), Arg.Any<CancellationToken>())
            .Returns(call => new PricingContext(
                VatRatePercent: 7.5m,
                PricesIncludeVat: false,
                Zone: call.ArgAt<DeliveryZone?>(0),
                DefaultDeliveryCharge: Money.Taka(500m),
                DeliveryFeeOverride: call.ArgAt<Money?>(1)));

        _variants.GetWithProductAsync(VariantId, Arg.Any<CancellationToken>()).Returns(Variant());

        _quotations.InsertAsync(Arg.Any<Quotation>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call => _inserted.Add(call.Arg<Quotation>()));

        _orders.InsertAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call => _placed.Add(call.Arg<Order>()));

        _payments.ResolveAsync(
                Arg.Any<string>(), Arg.Any<Money>(), Arg.Any<DeliveryZone>(), Arg.Any<CancellationToken>())
            .Returns(new EligiblePaymentMethod(
                new PaymentMethodConfig { Code = PaymentMethodCodes.CashOnDelivery },
                Substitute.For<IPaymentProvider>(),
                Money.Taka(50m)));

        _inventory.ReserveForOrderAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>())
            .Returns(GeneralResponse.Success());

        _unitOfWork
            .ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<GeneralResponse<QuotationDto>>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
                call.Arg<Func<CancellationToken, Task<GeneralResponse<QuotationDto>>>>()(
                    CancellationToken.None));

        _unitOfWork
            .ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<GeneralResponse<PlacedOrderDto>>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
                call.Arg<Func<CancellationToken, Task<GeneralResponse<PlacedOrderDto>>>>()(
                    CancellationToken.None));

        _service = new QuotationService(
            _quotations, _orders, _variants, _pricing, _payments, _numbers, _notifications,
            _inventory, _settings, _currentUser, _clock, _unitOfWork,
            NullLogger<QuotationService>.Instance);
    }

    // -------------------------------------------------------------------------
    // Writing one
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_quotation_prices_itself_with_the_same_function_a_basket_uses()
    {
        var result = await _service.SaveAsync(null, Request());

        result.IsSuccess.ShouldBeTrue(result.Message);

        var quotation = _inserted.ShouldHaveSingleItem();

        // Two beds at 30,000, VAT on top at 7.5%, and delivery charged per unit
        // — 500 each, because two beds are two vans' worth of trouble and the
        // pricer has always said so.
        quotation.Subtotal.ShouldBe(Money.Taka(60_000m));
        quotation.VatAmount.ShouldBe(Money.Taka(4_500m));
        quotation.DeliveryFee.ShouldBe(Money.Taka(1_000m));
        quotation.GrandTotal.ShouldBe(Money.Taka(65_500m));

        quotation.Status.ShouldBe(QuotationStatus.Draft);
    }

    [Fact]
    public async Task A_catalogue_line_takes_its_name_and_price_from_the_catalogue()
    {
        await _service.SaveAsync(null, Request());

        var line = _inserted.ShouldHaveSingleItem().Lines.ShouldHaveSingleItem();

        line.Description.ShouldBe("Segun king bed");
        line.Sku.ShouldBe("BED-SEGUN-K");
        line.UnitPrice.ShouldBe(Money.Taka(30_000m));
        line.HoldsStock.ShouldBe(true);
    }

    [Fact]
    public async Task But_a_negotiated_price_is_ordinary_and_is_kept()
    {
        await _service.SaveAsync(null, Request(unitPrice: 27_500m));

        _inserted.ShouldHaveSingleItem().Lines.ShouldHaveSingleItem()
            .UnitPrice.ShouldBe(Money.Taka(27_500m));
    }

    [Fact]
    public async Task A_wardrobe_built_to_an_alcove_can_be_quoted_for()
    {
        // The most valuable thing a designer sells has no variant and never
        // will. A quotation that could not carry it would be a quotation for
        // the cheap half of the job.
        var result = await _service.SaveAsync(null, Request(line: new SaveQuotationLineDto
        {
            Description = "Wardrobe, 7ft, segun, built to the alcove",
            Quantity = 1,
            UnitPrice = 85_000m,
            LeadTimeDays = 30
        }));

        result.IsSuccess.ShouldBeTrue(result.Message);

        var line = _inserted.ShouldHaveSingleItem().Lines.ShouldHaveSingleItem();

        line.ProductVariantId.ShouldBeNull();
        line.HoldsStock.ShouldBe(false);
        line.LeadTimeDays.ShouldBe(30);
    }

    [Fact]
    public async Task A_line_that_is_not_from_the_catalogue_needs_a_price()
    {
        var result = await _service.SaveAsync(null, Request(line: new SaveQuotationLineDto
        {
            Description = "Wardrobe to the alcove",
            Quantity = 1
        }));

        result.ErrorCode.ShouldBe(QuotationErrors.PriceInvalid);
    }

    [Fact]
    public async Task And_a_description()
    {
        var result = await _service.SaveAsync(null, Request(line: new SaveQuotationLineDto
        {
            Quantity = 1,
            UnitPrice = 85_000m
        }));

        result.ErrorCode.ShouldBe(QuotationErrors.DescriptionRequired);
    }

    [Fact]
    public async Task A_discount_larger_than_the_goods_is_refused()
    {
        var result = await _service.SaveAsync(null, Request(discount: 100_000m));

        result.ErrorCode.ShouldBe(QuotationErrors.DiscountTooLarge);
        _inserted.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_quotation_the_customer_is_holding_cannot_be_edited()
    {
        _quotations.GetDetailAsync(7, Arg.Any<CancellationToken>())
            .Returns(Existing(QuotationStatus.Sent));

        var result = await _service.SaveAsync(7, Request());

        // Pulling it back to Draft is the way, and that is a deliberate act
        // that leaves a trail.
        result.ErrorCode.ShouldBe(QuotationErrors.NotEditable);
    }

    // -------------------------------------------------------------------------
    // Sending it
    // -------------------------------------------------------------------------

    [Fact]
    public async Task One_with_nowhere_to_deliver_to_cannot_be_sent()
    {
        var quotation = Existing(QuotationStatus.Draft);

        quotation.ShippingAddress = null;

        _quotations.GetByNumberAsync(Number, Arg.Any<CancellationToken>()).Returns(quotation);

        var result = await _service.SetStatusAsync(Number, QuotationStatus.Sent, null);

        // Delivery cannot be priced without one, so the figure would be a lie.
        result.ErrorCode.ShouldBe(QuotationErrors.AddressRequired);
    }

    [Fact]
    public async Task Sending_it_tells_the_customer_and_stamps_the_moment()
    {
        var quotation = Existing(QuotationStatus.Draft);

        _quotations.GetByNumberAsync(Number, Arg.Any<CancellationToken>()).Returns(quotation);

        NotificationRequest? sent = null;

        await _notifications.EnqueueAsync(
            Arg.Do<NotificationRequest>(request => sent = request), Arg.Any<CancellationToken>());

        var result = await _service.SetStatusAsync(Number, QuotationStatus.Sent, null);

        result.IsSuccess.ShouldBeTrue(result.Message);
        quotation.Status.ShouldBe(QuotationStatus.Sent);
        quotation.SentAt.ShouldBe(_clock.UtcNow);

        sent.ShouldNotBeNull();
        sent.Type.ShouldBe("quotation.sent");
    }

    [Fact]
    public async Task Re_sending_a_revised_one_clears_the_refusal_it_already_had()
    {
        var quotation = Existing(QuotationStatus.Draft);

        quotation.DeclineReason = "Too expensive.";
        quotation.RespondedAt = _clock.UtcNow.AddDays(-2);

        _quotations.GetByNumberAsync(Number, Arg.Any<CancellationToken>()).Returns(quotation);

        await _service.SetStatusAsync(Number, QuotationStatus.Sent, null);

        // An old refusal beside a fresh quotation reads as the customer having
        // turned down the one they are being shown.
        quotation.DeclineReason.ShouldBeNull();
        quotation.RespondedAt.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // The customer answering
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_guest_accepts_by_quoting_the_number_they_were_given()
    {
        var quotation = Existing(QuotationStatus.Sent);

        _quotations.GetByNumberAsync(Number, Arg.Any<CancellationToken>()).Returns(quotation);

        var result = await _service.AcceptAsync(Number, "01712349999");

        result.IsSuccess.ShouldBeTrue(result.Message);
        quotation.Status.ShouldBe(QuotationStatus.Accepted);
        quotation.RespondedAt.ShouldBe(_clock.UtcNow);
    }

    [Fact]
    public async Task A_guessed_number_on_its_own_discloses_nothing()
    {
        _quotations.GetByNumberAsync(Number, Arg.Any<CancellationToken>())
            .Returns(Existing(QuotationStatus.Sent));

        var result = await _service.GetAsync(Number, contactPhone: null);

        result.ErrorCode.ShouldBe(QuotationErrors.NotFound);
    }

    [Fact]
    public async Task A_draft_does_not_exist_as_far_as_the_customer_is_concerned()
    {
        _quotations.GetByNumberAsync(Number, Arg.Any<CancellationToken>())
            .Returns(Existing(QuotationStatus.Draft));

        // Not "you may not see this": it is the designer's working copy, and
        // the figures on it are nobody's promise yet.
        var result = await _service.GetAsync(Number, "01712349999");

        result.ErrorCode.ShouldBe(QuotationErrors.NotFound);
    }

    [Fact]
    public async Task One_that_has_run_out_is_re_quoted_rather_than_accepted()
    {
        var quotation = Existing(QuotationStatus.Sent);

        quotation.ValidUntil = _clock.DhakaToday.AddDays(-1);

        _quotations.GetByNumberAsync(Number, Arg.Any<CancellationToken>()).Returns(quotation);

        var result = await _service.AcceptAsync(Number, "01712349999");

        result.ErrorCode.ShouldBe(QuotationErrors.Expired);
        quotation.Status.ShouldBe(QuotationStatus.Sent);
    }

    [Fact]
    public async Task Declining_records_why_when_they_say_why()
    {
        var quotation = Existing(QuotationStatus.Sent);

        _quotations.GetByNumberAsync(Number, Arg.Any<CancellationToken>()).Returns(quotation);

        await _service.DeclineAsync(Number, "01712349999", "Found it cheaper elsewhere.");

        quotation.Status.ShouldBe(QuotationStatus.Declined);
        quotation.DeclineReason.ShouldBe("Found it cheaper elsewhere.");
    }

    [Fact]
    public async Task The_customers_copy_withholds_the_designers_own_notes()
    {
        var quotation = Existing(QuotationStatus.Sent);

        quotation.InternalNotes = "She will go to 80 if pushed.";

        _quotations.GetByNumberAsync(Number, Arg.Any<CancellationToken>()).Returns(quotation);

        var mine = await _service.GetAsync(Number, "01712349999");
        var theirs = await _service.GetForStaffAsync(Number);

        mine.Data!.InternalNotes.ShouldBeNull();
        mine.Data.ContactPhone.ShouldContain("*");

        theirs.Data!.InternalNotes.ShouldBe("She will go to 80 if pushed.");
        theirs.Data.ContactPhone.ShouldBe("+8801712349999");
    }

    // -------------------------------------------------------------------------
    // Becoming an order
    // -------------------------------------------------------------------------

    [Fact]
    public async Task The_ways_to_pay_are_priced_against_the_quotation_not_a_basket()
    {
        // The checkout's own list prices the methods against the caller's
        // cart, and whoever is converting a quotation has no cart. What
        // decides here is the quotation's total — a three-lakh one may be over
        // the cash-on-delivery ceiling — and where it is going.
        var quotation = Existing(QuotationStatus.Accepted);

        _quotations.GetByNumberAsync(Number, Arg.Any<CancellationToken>()).Returns(quotation);

        _payments.GetEligibleAsync(
                Arg.Any<Money>(), Arg.Any<DeliveryZone>(), Arg.Any<CancellationToken>())
            .Returns([
                new EligiblePaymentMethod(
                    new PaymentMethodConfig
                    {
                        Code = PaymentMethodCodes.CashOnDelivery,
                        DisplayName = LocalizedText.Create("Cash on delivery")
                    },
                    Substitute.For<IPaymentProvider>(),
                    Money.Taka(50m))
            ]);

        var result = await _service.GetPaymentMethodsAsync(Number);

        result.IsSuccess.ShouldBeTrue(result.Message);
        result.Data.ShouldHaveSingleItem().Surcharge.ShouldBe(50m);

        await _payments.Received(1).GetEligibleAsync(
            quotation.GrandTotal, quotation.DeliveryZone, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Converting_honours_the_figure_the_customer_agreed_to()
    {
        var quotation = Existing(QuotationStatus.Accepted);

        _quotations.GetByNumberAsync(Number, Arg.Any<CancellationToken>()).Returns(quotation);

        var result = await _service.ConvertAsync(Number, new ConvertQuotationDto());

        result.IsSuccess.ShouldBeTrue(result.Message);

        var order = _placed.ShouldHaveSingleItem();

        // Every figure copied, including the rate. A quotation given last month
        // at 5% must not become an order at today's 7.5%.
        order.Subtotal.ShouldBe(quotation.Subtotal);
        order.VatAmount.ShouldBe(quotation.VatAmount);
        order.VatRatePercent.ShouldBe(quotation.VatRatePercent);
        order.DeliveryFee.ShouldBe(quotation.DeliveryFee);

        // Plus what paying that way costs, which is not part of what was
        // quoted and which the quotation says so.
        order.PaymentSurcharge.ShouldBe(Money.Taka(50m));
        order.GrandTotal.ShouldBe(quotation.GrandTotal + Money.Taka(50m));

        quotation.Status.ShouldBe(QuotationStatus.Converted);
        quotation.ConvertedOrderId.ShouldBe(order.Id);
    }

    [Fact]
    public async Task The_order_says_what_the_customer_was_quoted_word_for_word()
    {
        var quotation = Existing(QuotationStatus.Accepted);

        quotation.Lines =
        [
            new QuotationLine
            {
                Description = "Wardrobe, 7ft, segun, built to the alcove",
                Quantity = 1,
                UnitPrice = Money.Taka(85_000m),
                LineTotal = Money.Taka(85_000m),
                LeadTimeDays = 30
            }
        ];

        _quotations.GetByNumberAsync(Number, Arg.Any<CancellationToken>()).Returns(quotation);

        await _service.ConvertAsync(Number, new ConvertQuotationDto());

        var line = _placed.ShouldHaveSingleItem().Lines.ShouldHaveSingleItem();

        // No variant, so nothing to hold — and the invoice reads as the
        // quotation did rather than as a catalogue entry that does not exist.
        line.ProductVariantId.ShouldBeNull();
        line.ProductNameEn.ShouldBe("Wardrobe, 7ft, segun, built to the alcove");
        line.LeadTimeDays.ShouldBe(30);
    }

    [Fact]
    public async Task A_second_press_of_Convert_cannot_make_a_second_order()
    {
        var quotation = Existing(QuotationStatus.Accepted);

        _quotations.GetByNumberAsync(Number, Arg.Any<CancellationToken>()).Returns(quotation);

        await _service.ConvertAsync(Number, new ConvertQuotationDto());

        // Keyed on the quotation, so the unique index settles the race the
        // status check cannot.
        _placed.ShouldHaveSingleItem().IdempotencyKey.ShouldBe($"quotation:{Number}");
    }

    [Fact]
    public async Task One_the_customer_has_not_accepted_is_not_converted()
    {
        _quotations.GetByNumberAsync(Number, Arg.Any<CancellationToken>())
            .Returns(Existing(QuotationStatus.Sent));

        var result = await _service.ConvertAsync(Number, new ConvertQuotationDto());

        result.ErrorCode.ShouldBe(QuotationErrors.NotAccepted);
        _placed.ShouldBeEmpty();
    }

    [Fact]
    public async Task And_one_already_converted_is_not_converted_twice()
    {
        var quotation = Existing(QuotationStatus.Converted);

        quotation.ConvertedOrderId = 9;

        _quotations.GetByNumberAsync(Number, Arg.Any<CancellationToken>()).Returns(quotation);

        var result = await _service.ConvertAsync(Number, new ConvertQuotationDto());

        result.ErrorCode.ShouldBe(QuotationErrors.AlreadyConverted);
        _placed.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_shelf_that_cannot_cover_it_takes_the_whole_conversion_down()
    {
        _quotations.GetByNumberAsync(Number, Arg.Any<CancellationToken>())
            .Returns(Existing(QuotationStatus.Accepted));

        _inventory.ReserveForOrderAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>())
            .Returns(GeneralResponse.Invalid(
                InventoryErrors.InsufficientStock,
                "That bed has just sold out.",
                new Dictionary<string, string[]>()));

        var result = await _service.ConvertAsync(Number, new ConvertQuotationDto());

        // An order that exists for a bed that does not is a phone call the
        // shop should not have to make.
        result.ErrorCode.ShouldBe(InventoryErrors.InsufficientStock);
    }

    [Fact]
    public async Task Converting_tells_the_customer_the_order_number_and_not_the_price_again()
    {
        _quotations.GetByNumberAsync(Number, Arg.Any<CancellationToken>())
            .Returns(Existing(QuotationStatus.Accepted));

        NotificationRequest? sent = null;

        await _notifications.EnqueueAsync(
            Arg.Do<NotificationRequest>(request => sent = request), Arg.Any<CancellationToken>());

        await _service.ConvertAsync(Number, new ConvertQuotationDto());

        // One message about one purchase. The customer has already read these
        // figures on the quotation.
        sent.ShouldNotBeNull();
        sent.Type.ShouldBe("quotation.converted");
    }

    // -------------------------------------------------------------------------
    // Fixtures
    // -------------------------------------------------------------------------

    private static SaveQuotationDto Request(
        decimal discount = 0m,
        decimal? unitPrice = null,
        SaveQuotationLineDto? line = null) =>
        new()
        {
            ContactName = "Ayesha Siddiqua",
            ContactPhone = "01712349999",
            ShippingAddress = new DeliveryAddressDto
            {
                Division = "Dhaka",
                District = "Dhaka",
                Area = "Dhanmondi",
                AddressLine = "House 4, Road 27"
            },
            Discount = discount,
            Lines =
            [
                line ?? new SaveQuotationLineDto
                {
                    ProductVariantId = VariantId,
                    Quantity = 2,
                    UnitPrice = unitPrice
                }
            ]
        };

    private static ProductVariant Variant() =>
        new()
        {
            Id = VariantId,
            Sku = "BED-SEGUN-K",
            VariantName = "Segun · King",
            PriceOverride = Money.Taka(30_000m),
            Product = new Product
            {
                Id = 5,
                Name = LocalizedText.Create("Segun king bed"),
                Slug = Slug.From("segun-king-bed"),
                BasePrice = Money.Taka(30_000m)
            }
        };

    private static Quotation Existing(QuotationStatus status) =>
        new()
        {
            Id = 7,
            QuotationNumber = Number,
            ContactName = "Ayesha Siddiqua",
            ContactPhone = "+8801712349999",
            ShippingAddress = DeliveryAddress.Create(
                "Dhaka", "Dhaka", "House 4, Road 27", area: "Dhanmondi"),
            DeliveryZone = DeliveryZone.InsideDhaka,
            Currency = Money.Bdt,
            Subtotal = Money.Taka(60_000m),
            DiscountTotal = Money.Zero(Money.Bdt),
            GoodsNet = Money.Taka(60_000m),
            VatAmount = Money.Taka(4_500m),
            VatRatePercent = 7.5m,
            PricesIncludeVat = false,
            DeliveryFee = Money.Taka(500m),
            GrandTotal = Money.Taka(65_000m),
            ValidUntil = new DateOnly(2026, 10, 15),
            Status = status,
            Lines =
            [
                new QuotationLine
                {
                    ProductVariantId = VariantId,
                    ProductId = 5,
                    Description = "Segun king bed",
                    Sku = "BED-SEGUN-K",
                    Quantity = 2,
                    UnitPrice = Money.Taka(30_000m),
                    LineTotal = Money.Taka(60_000m)
                }
            ]
        };
}
