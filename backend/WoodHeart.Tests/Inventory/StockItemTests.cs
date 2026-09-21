using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Inventory;
using WoodHeart.Domain.Enums.Inventory;
using WoodHeart.Domain.Exceptions;

namespace WoodHeart.Tests.Inventory;

/// <summary>
/// The ledger arithmetic, with no database and no service.
/// </summary>
/// <remarks>
/// PLAN §12 names stock reservation as non-negotiable coverage: it is one
/// of the three places money is lost silently. Every method that changes a
/// count is here, with the case that would lose money if it were wrong.
/// </remarks>
public class StockItemTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);

    private static StockItem Item(int onHand, int reserved = 0) =>
        new() { Id = 1, ProductVariantId = 7, OnHand = onHand, Reserved = reserved };

    // --- Reserving -----------------------------------------------------------

    [Fact]
    public void Reserving_holds_units_without_moving_them()
    {
        var item = Item(onHand: 5);

        item.Reserve(2);

        // Nothing left the shelf: OnHand is what is physically there.
        item.OnHand.ShouldBe(5);
        item.Reserved.ShouldBe(2);
        item.Available.ShouldBe(3);
    }

    [Fact]
    public void Reserving_more_than_is_available_is_refused_and_holds_nothing()
    {
        var item = Item(onHand: 5, reserved: 4);

        var ex = Should.Throw<DomainException>(() => item.Reserve(2));

        ex.Code.ShouldBe(InventoryErrors.InsufficientStock);
        item.Reserved.ShouldBe(4);
    }

    [Fact]
    public void The_last_unit_can_be_reserved_and_then_no_more()
    {
        var item = Item(onHand: 1);

        item.Reserve(1);

        item.Available.ShouldBe(0);
        Should.Throw<DomainException>(() => item.Reserve(1));
    }

    [Fact]
    public void Releasing_gives_the_hold_back()
    {
        var item = Item(onHand: 5, reserved: 3);

        item.Release(2);

        item.Reserved.ShouldBe(1);
        item.Available.ShouldBe(4);
        item.OnHand.ShouldBe(5);
    }

    [Fact]
    public void Releasing_more_than_is_held_clamps_rather_than_going_negative()
    {
        // The books were already wrong; refusing to fix them helps nobody.
        var item = Item(onHand: 5, reserved: 1);

        item.Release(3);

        item.Reserved.ShouldBe(0);
    }

    // --- Committing a sale ---------------------------------------------------

    [Fact]
    public void Shipping_takes_the_units_off_the_shelf_and_writes_a_sale()
    {
        var item = Item(onHand: 5, reserved: 2);

        var movement = item.Commit(2, orderId: 42, "Rakib", Now);

        item.OnHand.ShouldBe(3);
        item.Reserved.ShouldBe(0);
        movement.Type.ShouldBe(StockMovementType.Sale);
        movement.Quantity.ShouldBe(-2);
        movement.OnHandAfter.ShouldBe(3);
        movement.OrderId.ShouldBe(42);
        movement.PerformedBy.ShouldBe("Rakib");
        movement.OccurredAt.ShouldBe(Now);
    }

    [Fact]
    public void Shipping_more_than_is_on_hand_is_refused()
    {
        var item = Item(onHand: 1, reserved: 1);

        var ex = Should.Throw<DomainException>(() => item.Commit(2, 42, "Rakib", Now));

        ex.Code.ShouldBe(InventoryErrors.WouldGoNegative);
        item.OnHand.ShouldBe(1);
    }

    // --- Movements by hand ---------------------------------------------------

    [Theory]
    [InlineData(StockMovementType.Purchase, 3, 8)]
    [InlineData(StockMovementType.Return, 1, 6)]
    [InlineData(StockMovementType.TransferIn, 2, 7)]
    [InlineData(StockMovementType.Damage, 2, 3)]
    [InlineData(StockMovementType.TransferOut, 5, 0)]
    public void The_type_decides_the_sign(StockMovementType type, int quantity, int onHandAfter)
    {
        // A caller cannot record a Purchase that deducts or a Damage that
        // adds; the quantity is "how many" and the type says which way.
        var item = Item(onHand: 5);

        var movement = item.Apply(type, quantity, "Rakib", Now, reason: "test");

        item.OnHand.ShouldBe(onHandAfter);
        movement.OnHandAfter.ShouldBe(onHandAfter);
        movement.Quantity.ShouldBe(onHandAfter - 5);
    }

    [Theory]
    [InlineData(-2, 3)]
    [InlineData(4, 9)]
    public void An_adjustment_is_signed(int quantity, int onHandAfter)
    {
        var item = Item(onHand: 5);

        item.Apply(StockMovementType.Adjustment, quantity, "Rakib", Now, reason: "recount");

        item.OnHand.ShouldBe(onHandAfter);
    }

    [Fact]
    public void An_adjustment_of_zero_is_refused()
    {
        var item = Item(onHand: 5);

        Should.Throw<DomainException>(() => item.Apply(StockMovementType.Adjustment, 0, "Rakib", Now))
            .Code.ShouldBe(InventoryErrors.QuantityInvalid);
    }

    [Theory]
    [InlineData(StockMovementType.Purchase)]
    [InlineData(StockMovementType.Damage)]
    public void A_zero_or_negative_quantity_is_refused_for_the_unsigned_types(StockMovementType type)
    {
        var item = Item(onHand: 5);

        Should.Throw<DomainException>(() => item.Apply(type, 0, "Rakib", Now));
        Should.Throw<DomainException>(() => item.Apply(type, -1, "Rakib", Now));
        item.OnHand.ShouldBe(5);
    }

    [Fact]
    public void A_write_off_below_zero_is_refused()
    {
        // "We have fewer than the system says" is an adjustment down to what
        // is there, not past it.
        var item = Item(onHand: 2);

        var ex = Should.Throw<DomainException>(() => item.Apply(StockMovementType.Damage, 3, "Rakib", Now));

        ex.Code.ShouldBe(InventoryErrors.WouldGoNegative);
        item.OnHand.ShouldBe(2);
    }

    [Fact]
    public void A_sale_cannot_be_recorded_by_hand()
    {
        // Sales come from shipping orders, so the ledger and the order book
        // cannot disagree about what was sold.
        var item = Item(onHand: 5);

        Should.Throw<DomainException>(() => item.Apply(StockMovementType.Sale, 1, "Rakib", Now))
            .Code.ShouldBe(InventoryErrors.TypeInvalid);
    }

    [Fact]
    public void Damage_to_reserved_units_leaves_available_negative_rather_than_hiding_it()
    {
        // Two are held for an order; both get water-damaged. The count is
        // truthful — OnHand 0, Reserved 2 — and Available says the order
        // cannot be filled. Hiding that would be worse.
        var item = Item(onHand: 2, reserved: 2);

        item.Apply(StockMovementType.Damage, 2, "Rakib", Now, reason: "flood");

        item.OnHand.ShouldBe(0);
        item.Available.ShouldBe(-2);
    }

    [Fact]
    public void Every_movement_carries_the_running_count_and_the_provenance()
    {
        var item = Item(onHand: 0);

        var movement = item.Apply(
            StockMovementType.Purchase, 12, "Rakib", Now, reason: "First delivery", reference: "INV-0042");

        movement.StockItem.ShouldBeSameAs(item);
        movement.ProductVariantId.ShouldBe(7);
        movement.OnHandAfter.ShouldBe(12);
        movement.Reference.ShouldBe("INV-0042");
        movement.Reason.ShouldBe("First delivery");
    }
}
