namespace WoodHeart.Domain.Constants;

/// <summary>
/// Stable error codes for the cart and, later, orders.
/// </summary>
/// <remarks>
/// Same contract as <see cref="CatalogErrors"/>: the code is what the Angular
/// client branches on, the message is prose that will be reworded and
/// translated, and the suffix picks the HTTP status in
/// <c>BaseApiController.HandleResult</c>.
/// </remarks>
public static class OrderingErrors
{
    private const string Prefix = "ordering.";

    // --- Cart ----------------------------------------------------------------

    public const string CartNotFound = Prefix + "cart.not_found";
    public const string CartLineNotFound = Prefix + "cart_line.not_found";

    /// <summary>
    /// The cart has already become an order. Adding to it would change what
    /// someone has been charged for.
    /// </summary>
    public const string CartNotActive = Prefix + "cart.not_active.conflict";

    /// <summary>
    /// A guest with no anonymous id and no session. Without one there is
    /// nothing to attach a basket to, and every request would create a new one.
    /// </summary>
    public const string CartIdentityMissing = Prefix + "cart.identity_missing";

    public const string CartEmpty = Prefix + "cart.empty";

    // --- Line validity -------------------------------------------------------

    public const string QuantityInvalid = Prefix + "quantity_invalid";

    /// <summary>
    /// More units than a person plausibly buys in one order. A guard against a
    /// fat finger and against someone reserving the whole warehouse for free.
    /// </summary>
    public const string QuantityTooLarge = Prefix + "quantity_too_large";

    /// <summary>
    /// The variant exists but its product is not <c>Active</c> — a draft, or
    /// something withdrawn from sale while it sat in a basket.
    /// </summary>
    public const string ProductNotPurchasable = Prefix + "product_not_purchasable.conflict";

    /// <summary>Adding a line priced in a different currency than the cart holds.</summary>
    public const string CurrencyMismatch = Prefix + "currency_mismatch.conflict";
}
