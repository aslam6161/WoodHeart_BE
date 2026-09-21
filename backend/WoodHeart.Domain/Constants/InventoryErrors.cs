namespace WoodHeart.Domain.Constants;

/// <summary>
/// Stable error codes for stock.
/// </summary>
/// <remarks>
/// Same contract as <see cref="OrderingErrors"/>: the client branches on the
/// code, the message is prose, and the suffix picks the HTTP status.
/// </remarks>
public static class InventoryErrors
{
    private const string Prefix = "inventory.";

    /// <summary>
    /// Fewer units available than the order wants. A conflict, not a
    /// validation failure: the basket was fine when it was filled, and the
    /// world moved underneath it.
    /// </summary>
    public const string InsufficientStock = "ordering.insufficient_stock.conflict";

    /// <summary>The variant has never been stocked. There is nothing to move.</summary>
    public const string StockItemNotFound = Prefix + "stock_item.not_found";

    public const string VariantNotFound = Prefix + "variant.not_found";

    /// <summary>
    /// A made-to-order or service product. It has no shelf, so it has no
    /// count; a stock-in against it would be a number nothing reads.
    /// </summary>
    public const string VariantNotStocked = Prefix + "variant_not_stocked";

    public const string QuantityInvalid = Prefix + "quantity_invalid";

    public const string TypeInvalid = Prefix + "type_invalid";

    /// <summary>Every adjustment says why. "Damage" alone is not a reason.</summary>
    public const string ReasonRequired = Prefix + "reason_required";

    /// <summary>
    /// The movement would take <c>OnHand</c> below zero. A count cannot be
    /// negative; if the shelf is emptier than the system says, that is an
    /// adjustment down to what is there, not past it.
    /// </summary>
    public const string WouldGoNegative = Prefix + "would_go_negative.conflict";
}
