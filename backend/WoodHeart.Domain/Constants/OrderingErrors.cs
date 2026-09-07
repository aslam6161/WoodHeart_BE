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

    // --- Checkout ------------------------------------------------------------

    /// <summary>
    /// Something in the basket cannot be bought any more.
    /// </summary>
    /// <remarks>
    /// The cart page shows such a line greyed out and prices around it; checkout
    /// refuses outright. The difference is deliberate — browsing past an
    /// unavailable item is fine, placing an order that silently drops one is
    /// how a customer receives two of the three things they paid for.
    /// </remarks>
    public const string LineNotPurchasable = Prefix + "line_not_purchasable.conflict";

    /// <summary>The phone number could not be read as a Bangladeshi mobile number.</summary>
    public const string ContactPhoneInvalid = Prefix + "contact_phone_invalid";

    /// <summary>The delivery address is missing something a rider would need.</summary>
    public const string AddressInvalid = Prefix + "address_invalid";

    // --- Orders --------------------------------------------------------------

    public const string OrderNotFound = Prefix + "order.not_found";

    /// <summary>
    /// The order exists, but not for whoever is asking.
    /// </summary>
    /// <remarks>
    /// Returned as a not-found rather than a forbidden, so the endpoint cannot
    /// be used to confirm which order numbers exist.
    /// </remarks>
    public const string OrderNotYours = Prefix + "order.not_found";

    /// <summary>The move is not one the status machine allows from here.</summary>
    public const string OrderTransitionInvalid = Prefix + "order.transition_invalid.conflict";

    /// <summary>Past the point where a customer can stop it themselves.</summary>
    public const string OrderNotCancellable = Prefix + "order.not_cancellable.conflict";

    // --- Admin order management ----------------------------------------------

    /// <summary>The move is not one <c>PaymentStatusMachine</c> allows from here.</summary>
    public const string PaymentTransitionInvalid = Prefix + "order.payment_transition_invalid.conflict";

    /// <summary>
    /// The total can no longer be edited, because money has already changed
    /// hands or the goods have already left.
    /// </summary>
    /// <remarks>
    /// Editing it anyway would leave the order saying one thing and the till
    /// saying another. The correction from here is a refund or a second
    /// collection, both of which leave a record.
    /// </remarks>
    public const string OrderAmountLocked = Prefix + "order.amount_locked.conflict";

    /// <summary>A staff-facing change that must say why, and did not.</summary>
    public const string ReasonRequired = Prefix + "reason_required";
}
