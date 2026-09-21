using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.Entity.Identity;
using WoodHeart.Domain.Entity.Inventory;
using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Enums.Catalog;
using WoodHeart.Domain.Enums.Inventory;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Catalog;
using WoodHeart.Repository.Interfaces.Inventory;
using WoodHeart.Repository.Interfaces.Ordering;
using WoodHeart.Service.DTOs.Inventory;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Services.Inventory;
using WoodHeart.Tests.Helper;

namespace WoodHeart.Tests.Inventory;

/// <summary>
/// What the order lifecycle does to the shelf, and what the admin may do by hand.
/// </summary>
public class InventoryServiceTests
{
    private readonly IStockRepository _stock = Substitute.For<IStockRepository>();
    private readonly IProductVariantRepository _variants = Substitute.For<IProductVariantRepository>();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IStoreSettingService _settings = Substitute.For<IStoreSettingService>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly UserManager<AppUser> _users = FakeUserManager();
    private readonly FakeClock _clock = new();

    private readonly List<StockReservation> _reservations = [];
    private readonly List<StockMovement> _movements = [];

    private const long BedVariant = 1;
    private const long TableVariant = 2;
    private const long WardrobeVariant = 3; // made to order

    public InventoryServiceTests()
    {
        _stock.AddReservationAsync(Arg.Do<StockReservation>(_reservations.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _stock.AddMovementAsync(Arg.Do<StockMovement>(_movements.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // The bed and the table are stocked products; the wardrobe is built to order.
        _stock.GetStockedVariantIdsAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyCollection<long>>()
                .Where(id => id is BedVariant or TableVariant)
                .ToHashSet());

        _settings.GetIntAsync(SettingKeys.LowStockThreshold, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(5);
    }

    private InventoryService CreateService() =>
        new(_stock, _variants, _orders, _unitOfWork, _settings, _currentUser, _users, _clock,
            NullLogger<InventoryService>.Instance);

    // --- Reserving at placement ----------------------------------------------

    [Fact]
    public async Task Placing_an_order_holds_stock_for_each_stocked_line()
    {
        var bed = Item(BedVariant, onHand: 3);
        var table = Item(TableVariant, onHand: 10);
        GivenCounts(bed, table);

        var order = Order(status: OrderStatus.Pending, (BedVariant, 1), (TableVariant, 2), (WardrobeVariant, 1));

        var result = await CreateService().ReserveForOrderAsync(order);

        result.IsSuccess.ShouldBeTrue();
        bed.Reserved.ShouldBe(1);
        table.Reserved.ShouldBe(2);

        // One hold per stocked line, bound to the order; none for the
        // wardrobe, which has no shelf.
        _reservations.Count.ShouldBe(2);
        _reservations.ShouldAllBe(r => r.OrderId == order.Id && r.Status == StockReservationStatus.Active);
        _reservations.ShouldNotContain(r => r.ProductVariantId == WardrobeVariant);

        // Staged, not committed: the placement owns the transaction.
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_line_that_cannot_be_filled_refuses_the_whole_order_and_names_the_line()
    {
        var bed = Item(BedVariant, onHand: 1);
        var table = Item(TableVariant, onHand: 10);
        GivenCounts(bed, table);

        var order = Order(OrderStatus.Pending, (TableVariant, 2), (BedVariant, 2));

        var result = await CreateService().ReserveForOrderAsync(order);

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(InventoryErrors.InsufficientStock);
        result.Message.ShouldContain("Only 1");
        result.Errors.ShouldNotBeNull();
        result.Errors.Keys.ShouldBe([BedVariant.ToString()]);
    }

    [Fact]
    public async Task A_variant_nobody_has_stocked_is_sold_out_not_unlimited()
    {
        // No StockItem row at all. The dangerous reading is "not tracked,
        // sell freely"; the right one is "none".
        GivenCounts();

        var order = Order(OrderStatus.Pending, (BedVariant, 1));

        var result = await CreateService().ReserveForOrderAsync(order);

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(InventoryErrors.InsufficientStock);
        result.Message.ShouldContain("sold out");
        _reservations.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_order_of_only_made_to_order_lines_touches_nothing()
    {
        var order = Order(OrderStatus.Pending, (WardrobeVariant, 1));

        var result = await CreateService().ReserveForOrderAsync(order);

        result.IsSuccess.ShouldBeTrue();
        await _stock.DidNotReceive().GetByVariantsAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>());
    }

    // --- The lifecycle -------------------------------------------------------

    [Fact]
    public async Task Shipping_turns_the_holds_into_sales()
    {
        var bed = Item(BedVariant, onHand: 3, reserved: 1);
        var order = Order(OrderStatus.Shipped, (BedVariant, 1));
        GivenHolds(order, Hold(bed, order, 1));

        await CreateService().ApplyStatusChangeAsync(order, OrderStatus.ReadyToShip, OrderStatus.Shipped, "Rakib");

        bed.OnHand.ShouldBe(2);
        bed.Reserved.ShouldBe(0);

        var sale = _movements.ShouldHaveSingleItem();
        sale.Type.ShouldBe(StockMovementType.Sale);
        sale.Quantity.ShouldBe(-1);
        sale.OrderId.ShouldBe(order.Id);
        sale.PerformedBy.ShouldBe("Rakib");
        sale.OccurredAt.ShouldBe(_clock.UtcNow);
    }

    [Fact]
    public async Task Cancelling_before_shipping_frees_the_hold_and_writes_no_movement()
    {
        // Nothing physically moved, so the ledger has nothing to say. The
        // count shows one more available; that is the whole effect.
        var bed = Item(BedVariant, onHand: 3, reserved: 1);
        var order = Order(OrderStatus.Cancelled, (BedVariant, 1));
        var hold = Hold(bed, order, 1);
        GivenHolds(order, hold);

        await CreateService().ApplyStatusChangeAsync(order, OrderStatus.Confirmed, OrderStatus.Cancelled, "Rakib");

        bed.OnHand.ShouldBe(3);
        bed.Reserved.ShouldBe(0);
        hold.Status.ShouldBe(StockReservationStatus.Released);
        hold.ResolvedAt.ShouldBe(_clock.UtcNow);
        _movements.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_return_puts_the_sold_units_back()
    {
        var bed = Item(BedVariant, onHand: 2);
        var order = Order(OrderStatus.Returned, (BedVariant, 1));
        var hold = Hold(bed, order, 1);
        hold.Status = StockReservationStatus.Committed;
        GivenHolds(order, hold);

        await CreateService().ApplyStatusChangeAsync(order, OrderStatus.Delivered, OrderStatus.Returned, "Rakib");

        bed.OnHand.ShouldBe(3);

        var movement = _movements.ShouldHaveSingleItem();
        movement.Type.ShouldBe(StockMovementType.Return);
        movement.Quantity.ShouldBe(1);
        movement.OrderId.ShouldBe(order.Id);
    }

    [Fact]
    public async Task Settling_a_hold_twice_does_nothing_the_second_time()
    {
        // Two operators, one order, both press Shipped. The second call
        // finds no active hold and the count does not move again.
        var bed = Item(BedVariant, onHand: 3, reserved: 1);
        var order = Order(OrderStatus.Shipped, (BedVariant, 1));
        GivenHolds(order, Hold(bed, order, 1));

        var service = CreateService();

        await service.ApplyStatusChangeAsync(order, OrderStatus.ReadyToShip, OrderStatus.Shipped, "Rakib");
        await service.ApplyStatusChangeAsync(order, OrderStatus.ReadyToShip, OrderStatus.Shipped, "Rakib");

        bed.OnHand.ShouldBe(2);
        _movements.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Processing)]
    [InlineData(OrderStatus.ReadyToShip)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Completed)]
    public async Task The_paperwork_moves_leave_the_shelf_alone(OrderStatus to)
    {
        var order = Order(to, (BedVariant, 1));

        await CreateService().ApplyStatusChangeAsync(order, OrderStatus.Pending, to, "Rakib");

        await _stock.DidNotReceive().GetReservationsForOrderAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    // --- By hand -------------------------------------------------------------

    [Fact]
    public async Task The_first_stock_in_creates_the_count()
    {
        GivenVariant(BedVariant, ProductType.Stocked);
        _stock.GetByVariantAsync(BedVariant, Arg.Any<CancellationToken>()).Returns((StockItem?)null);

        var result = await CreateService().AdjustAsync(BedVariant, new AdjustStockDto
        {
            Type = StockMovementType.Purchase,
            Quantity = 6,
            Reference = "INV-0042",
            ReorderLevel = 2
        });

        result.IsSuccess.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.IsStocked.ShouldBeTrue();
        result.Data.OnHand.ShouldBe(6);
        result.Data.Available.ShouldBe(6);
        result.Data.ReorderLevel.ShouldBe(2);
        result.Data.IsLow.ShouldBeFalse();

        await _stock.Received(1).InsertAsync(
            Arg.Is<StockItem>(i => i.ProductVariantId == BedVariant && i.OnHand == 6), Arg.Any<CancellationToken>());
        _movements.ShouldHaveSingleItem().Reference.ShouldBe("INV-0042");
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Damage_without_a_reason_is_refused()
    {
        GivenVariant(BedVariant, ProductType.Stocked);
        _stock.GetByVariantAsync(BedVariant, Arg.Any<CancellationToken>()).Returns(Item(BedVariant, onHand: 5));

        var result = await CreateService().AdjustAsync(BedVariant, new AdjustStockDto
        {
            Type = StockMovementType.Damage,
            Quantity = 1
        });

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(InventoryErrors.ReasonRequired);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_made_to_order_product_has_no_count_to_change()
    {
        GivenVariant(WardrobeVariant, ProductType.MadeToOrder);

        var result = await CreateService().AdjustAsync(WardrobeVariant, new AdjustStockDto
        {
            Type = StockMovementType.Purchase,
            Quantity = 1
        });

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(InventoryErrors.VariantNotStocked);
    }

    [Fact]
    public async Task A_sale_cannot_be_entered_by_hand()
    {
        GivenVariant(BedVariant, ProductType.Stocked);

        var result = await CreateService().AdjustAsync(BedVariant, new AdjustStockDto
        {
            Type = StockMovementType.Sale,
            Quantity = 1
        });

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(InventoryErrors.TypeInvalid);
    }

    [Fact]
    public async Task A_write_off_past_zero_is_refused_with_the_domain_code()
    {
        GivenVariant(BedVariant, ProductType.Stocked);
        _stock.GetByVariantAsync(BedVariant, Arg.Any<CancellationToken>()).Returns(Item(BedVariant, onHand: 2));

        var result = await CreateService().AdjustAsync(BedVariant, new AdjustStockDto
        {
            Type = StockMovementType.Damage,
            Quantity = 3,
            Reason = "flood"
        });

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(InventoryErrors.WouldGoNegative);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_level_says_low_against_the_store_threshold_when_the_item_has_none()
    {
        GivenVariant(BedVariant, ProductType.Stocked);
        _stock.GetByVariantAsync(BedVariant, Arg.Any<CancellationToken>())
            .Returns(Item(BedVariant, onHand: 7, reserved: 3)); // available 4, threshold 5

        var result = await CreateService().GetLevelAsync(BedVariant);

        result.Data.ShouldNotBeNull();
        result.Data.Available.ShouldBe(4);
        result.Data.IsLow.ShouldBeTrue();
    }

    // --- Helpers -------------------------------------------------------------

    private static StockItem Item(long variantId, int onHand, int reserved = 0) =>
        new() { Id = variantId * 100, ProductVariantId = variantId, OnHand = onHand, Reserved = reserved };

    private void GivenCounts(params StockItem[] items) =>
        _stock.GetByVariantsAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns(call => items
                .Where(i => call.Arg<IReadOnlyCollection<long>>().Contains(i.ProductVariantId))
                .ToList());

    private static StockReservation Hold(StockItem item, Order order, int quantity) =>
        new()
        {
            Id = order.Id * 1000 + item.ProductVariantId,
            StockItem = item,
            StockItemId = item.Id,
            ProductVariantId = item.ProductVariantId,
            OrderId = order.Id,
            Quantity = quantity
        };

    private void GivenHolds(Order order, params StockReservation[] holds) =>
        _stock.GetReservationsForOrderAsync(order.Id, Arg.Any<CancellationToken>())
            .Returns(holds.ToList());

    private void GivenVariant(long variantId, ProductType type)
    {
        var product = new Product
        {
            Id = variantId * 10,
            Code = $"WH-{variantId:000}",
            Name = LocalizedText.Create("Segun King Bed"),
            Slug = Slug.From("segun-king-bed"),
            BasePrice = Money.Taka(68_500m),
            ProductType = type,
            Status = ProductStatus.Active
        };

        _variants.GetWithProductAsync(variantId, Arg.Any<CancellationToken>())
            .Returns(new ProductVariant
            {
                Id = variantId,
                ProductId = product.Id,
                Product = product,
                Sku = $"SKU-{variantId}",
                VariantName = "Segun · 6ft",
                IsActive = true
            });
    }

    private static Order Order(OrderStatus status, params (long VariantId, int Quantity)[] lines) =>
        new()
        {
            Id = 42,
            OrderNumber = "WH-2609-00042",
            Status = status,
            ContactName = "Rakib Hasan",
            ContactPhone = "+8801712345678",
            ShippingAddress = DeliveryAddress.Create("Dhaka", "Dhaka", "House 12, Road 3", area: "Dhanmondi"),
            Currency = Money.Bdt,
            Lines =
            [
                .. lines.Select((l, i) => new OrderLine
                {
                    Id = i + 1,
                    ProductVariantId = l.VariantId,
                    ProductId = l.VariantId * 10,
                    ProductNameEn = l.VariantId == BedVariant ? "Segun King Bed" : l.VariantId == TableVariant ? "Bedside Table" : "Wardrobe",
                    ProductSlug = "x",
                    Sku = $"SKU-{l.VariantId}",
                    VariantName = "Standard",
                    Quantity = l.Quantity,
                    UnitPrice = Money.Taka(1_000m),
                    DiscountAmount = Money.Zero(),
                    LineTotal = Money.Taka(1_000m * l.Quantity),
                    DeliveryChargeApplied = Money.Zero()
                })
            ]
        };

    private static UserManager<AppUser> FakeUserManager()
    {
        var store = Substitute.For<IUserStore<AppUser>>();
        var manager = Substitute.For<UserManager<AppUser>>(
            store, null, null, null, null, null, null, null, null);

        manager.FindByIdAsync(Arg.Any<string>()).Returns((AppUser?)null);

        return manager;
    }
}
