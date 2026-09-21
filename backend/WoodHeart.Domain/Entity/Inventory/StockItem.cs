using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.Enums.Inventory;
using WoodHeart.Domain.Exceptions;

namespace WoodHeart.Domain.Entity.Inventory;

/// <summary>
/// How many of one variant the shop has, and how many of those are spoken for.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>OnHand</c> is a projection of the ledger, not the truth.</b> Every
/// change to it goes through a method here that also produces the
/// <see cref="StockMovement"/> recording it, so "why does the system say 5
/// when we have 3 beds" is a question with an answer — the movements — rather
/// than a mystery. Nothing else sets <c>OnHand</c>; the setter is there for
/// the ORM.
/// </para>
/// <para>
/// <c>Reserved</c> is what placed-but-unshipped orders are holding. It is not
/// on the ledger because nothing has physically moved yet; the units are on
/// the shelf with somebody's name on them. <c>Available</c> is what a new
/// order may take.
/// </para>
/// <para>
/// One row per variant. There is one warehouse today; when there is a second,
/// this gains a location and the unique index widens. The methods would not
/// change.
/// </para>
/// </remarks>
public class StockItem : BaseEntity
{
    public long ProductVariantId { get; set; }

    public ProductVariant ProductVariant { get; set; } = null!;

    /// <summary>Units physically present. Never negative.</summary>
    public int OnHand { get; set; }

    /// <summary>Units held by orders that have not shipped.</summary>
    public int Reserved { get; set; }

    /// <summary>What a new order may take. Can go negative after damage to reserved units.</summary>
    public int Available => OnHand - Reserved;

    /// <summary>
    /// At or below this, the admin dashboard flags the line. Null uses the
    /// store-wide threshold.
    /// </summary>
    public int? ReorderLevel { get; set; }

    /// <summary>
    /// Holds units for an order. Refuses when fewer are available than asked
    /// for; the caller turns that into "only 2 left" for the customer.
    /// </summary>
    public void Reserve(int quantity)
    {
        DomainGuard.Positive(quantity, InventoryErrors.QuantityInvalid, "Quantity must be at least one.");

        if (quantity > Available)
        {
            throw new DomainException(
                InventoryErrors.InsufficientStock,
                $"Only {Math.Max(Available, 0)} available.");
        }

        Reserved += quantity;
    }

    /// <summary>Gives held units back, when the order that held them will not ship.</summary>
    public void Release(int quantity)
    {
        DomainGuard.Positive(quantity, InventoryErrors.QuantityInvalid, "Quantity must be at least one.");

        // Clamped rather than thrown. A release of more than is held means
        // the books were already wrong; refusing to fix them helps nobody.
        Reserved = Math.Max(Reserved - quantity, 0);
    }

    /// <summary>
    /// The order shipped: the held units leave the shelf. One Sale movement.
    /// </summary>
    public StockMovement Commit(int quantity, long orderId, string performedBy, DateTimeOffset occurredAt)
    {
        DomainGuard.Positive(quantity, InventoryErrors.QuantityInvalid, "Quantity must be at least one.");

        if (quantity > OnHand)
        {
            throw new DomainException(
                InventoryErrors.WouldGoNegative,
                $"Cannot ship {quantity}; only {OnHand} on hand.");
        }

        OnHand -= quantity;
        Reserved = Math.Max(Reserved - quantity, 0);

        return Movement(StockMovementType.Sale, -quantity, performedBy, occurredAt, orderId, reference: null, reason: null);
    }

    /// <summary>
    /// A movement that is not a sale: goods in, a return, a correction, a
    /// write-off, a transfer. The sign is decided by the type, so a caller
    /// cannot record a Purchase that deducts.
    /// </summary>
    /// <param name="quantity">
    /// How many. Positive for every type but <see cref="StockMovementType.Adjustment"/>,
    /// which is signed — a count correction goes either way.
    /// </param>
    public StockMovement Apply(
        StockMovementType type,
        int quantity,
        string performedBy,
        DateTimeOffset occurredAt,
        string? reason = null,
        string? reference = null,
        long? orderId = null)
    {
        var signed = type switch
        {
            StockMovementType.Purchase or StockMovementType.Return or StockMovementType.TransferIn
                => Positive(quantity),
            StockMovementType.Damage or StockMovementType.TransferOut
                => -Positive(quantity),
            StockMovementType.Adjustment
                => quantity == 0
                    ? throw new DomainException(InventoryErrors.QuantityInvalid, "An adjustment of zero changes nothing.")
                    : quantity,
            StockMovementType.Sale
                => throw new DomainException(
                    InventoryErrors.TypeInvalid, "A sale is recorded by shipping the order, not by hand."),
            _ => throw new DomainException(InventoryErrors.TypeInvalid, $"Unknown movement type {type}.")
        };

        if (OnHand + signed < 0)
        {
            throw new DomainException(
                InventoryErrors.WouldGoNegative,
                $"That would take stock to {OnHand + signed}; only {OnHand} on hand.");
        }

        OnHand += signed;

        return Movement(type, signed, performedBy, occurredAt, orderId, reference, reason);
    }

    private static int Positive(int quantity)
    {
        DomainGuard.Positive(quantity, InventoryErrors.QuantityInvalid, "Quantity must be at least one.");

        return quantity;
    }

    private StockMovement Movement(
        StockMovementType type,
        int signed,
        string performedBy,
        DateTimeOffset occurredAt,
        long? orderId,
        string? reference,
        string? reason) =>
        new()
        {
            StockItem = this,
            StockItemId = Id,
            ProductVariantId = ProductVariantId,
            Type = type,
            Quantity = signed,
            OnHandAfter = OnHand,
            OrderId = orderId,
            Reference = reference,
            Reason = reason,
            PerformedBy = performedBy,
            OccurredAt = occurredAt
        };
}

/// <summary>
/// One line of the ledger. Append-only: nothing edits or deletes a movement,
/// and the running <see cref="OnHandAfter"/> makes any point in the history
/// reconstructible without replaying it.
/// </summary>
public class StockMovement : BaseEntity
{
    public long StockItemId { get; set; }

    public StockItem StockItem { get; set; } = null!;

    /// <summary>Denormalised from the item, so the ledger can be listed by variant without a join.</summary>
    public long ProductVariantId { get; set; }

    public StockMovementType Type { get; set; }

    /// <summary>Signed: positive in, negative out.</summary>
    public int Quantity { get; set; }

    /// <summary>The count after this line was applied.</summary>
    public int OnHandAfter { get; set; }

    /// <summary>The order, for sales and returns.</summary>
    public long? OrderId { get; set; }

    /// <summary>A supplier's invoice number, a delivery note — whatever the paper says.</summary>
    public string? Reference { get; set; }

    /// <summary>Free text. Required for adjustments and damage.</summary>
    public string? Reason { get; set; }

    /// <summary>Who, as a name or phone; "system" for the order lifecycle.</summary>
    public string PerformedBy { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>
/// Units held for an order between placement and shipping.
/// </summary>
/// <remarks>
/// One per stocked order line. Bound to the order rather than the cart: a
/// basket does not hold stock — see PLAN §10.2 — because a browser comparing
/// three sofas must not lock three sofas. The hold starts when the customer
/// commits, and ends when the van leaves or the order dies.
/// </remarks>
public class StockReservation : BaseEntity
{
    public long StockItemId { get; set; }

    public StockItem StockItem { get; set; } = null!;

    public long ProductVariantId { get; set; }

    public long OrderId { get; set; }

    public int Quantity { get; set; }

    public StockReservationStatus Status { get; set; } = StockReservationStatus.Active;

    /// <summary>When the hold settled, one way or the other.</summary>
    public DateTimeOffset? ResolvedAt { get; set; }
}
