namespace WoodHeart.Domain.Constants;

/// <summary>
/// Stable error codes for discounts and coupons.
/// </summary>
/// <remarks>
/// <para>
/// Same contract as <see cref="OrderingErrors"/>: the client branches on the
/// code, the message is prose, and the suffix picks the HTTP status.
/// </para>
/// <para>
/// <b>The refusal codes are deliberately specific.</b> "That code cannot be
/// used" is the answer that makes a customer try it three more times and then
/// telephone the shop. "That code runs from the 1st of October" tells them
/// what to do, and every one of these distinctions already exists in the
/// engine — collapsing them on the way out would be throwing away information
/// the shop has already computed.
/// </para>
/// </remarks>
public static class PromotionErrors
{
    private const string Prefix = "promotions.";

    // --- Applying a coupon ----------------------------------------------------

    /// <summary>No discount has that code. Also covers an archived one.</summary>
    public const string CouponNotFound = Prefix + "coupon.not_found";

    /// <summary>The code is already on this basket.</summary>
    public const string CouponAlreadyApplied = Prefix + "coupon_already_applied.conflict";

    /// <summary>Draft, paused or archived. Not a customer's problem to solve.</summary>
    public const string CouponInactive = Prefix + "coupon_inactive";

    /// <summary>Its window has not opened yet.</summary>
    public const string CouponNotStarted = Prefix + "coupon_not_started";

    /// <summary>Its window has closed.</summary>
    public const string CouponExpired = Prefix + "coupon_expired";

    /// <summary>Nothing in the basket is what this discount applies to.</summary>
    public const string CouponNoEligibleItems = Prefix + "coupon_no_eligible_items";

    /// <summary>The basket is below the discount's minimum.</summary>
    public const string CouponMinSubtotal = Prefix + "coupon_min_subtotal";

    /// <summary>Fewer items than the discount asks for.</summary>
    public const string CouponMinQuantity = Prefix + "coupon_min_quantity";

    /// <summary>Not offered where this order is going.</summary>
    public const string CouponZone = Prefix + "coupon_zone";

    /// <summary>Tied to a payment method the customer has not chosen.</summary>
    public const string CouponPaymentMethod = Prefix + "coupon_payment_method";

    /// <summary>For new customers, and this one has ordered before.</summary>
    public const string CouponFirstOrderOnly = Prefix + "coupon_first_order_only";

    /// <summary>
    /// The code has been redeemed as many times as it was ever going to be.
    /// </summary>
    /// <remarks>
    /// A conflict rather than a validation failure: the request was correct and
    /// the world moved. The same code was working an hour ago.
    /// </remarks>
    public const string CouponLimitReached = Prefix + "coupon_limit_reached.conflict";

    /// <summary>This customer has used it as often as they may.</summary>
    public const string CouponCustomerLimitReached = Prefix + "coupon_customer_limit_reached.conflict";

    /// <summary>
    /// It qualifies, but something better is already on the basket and one of
    /// the two cannot be combined.
    /// </summary>
    public const string CouponNotCombinable = Prefix + "coupon_not_combinable";

    // --- Managing discounts ---------------------------------------------------

    public const string DiscountNotFound = Prefix + "discount.not_found";

    /// <summary>Another discount already uses that code.</summary>
    /// <remarks>
    /// Ends in <c>_taken</c>, which <c>BaseApiController</c> maps to a 409 —
    /// the same treatment a duplicate slug or SKU gets.
    /// </remarks>
    public const string CodeTaken = Prefix + "code_taken";

    public const string NameRequired = Prefix + "name_required";

    /// <summary>A percentage outside 0–100, or an amount that is not positive.</summary>
    public const string ValueInvalid = Prefix + "value_invalid";

    /// <summary>The window ends before it starts.</summary>
    public const string WindowInvalid = Prefix + "window_invalid";

    /// <summary>A target naming neither a product nor a category, or both.</summary>
    public const string TargetInvalid = Prefix + "target_invalid";

    /// <summary>A target pointing at a product or category that is not there.</summary>
    public const string TargetNotFound = Prefix + "target.not_found";

    /// <summary>
    /// The discount has been used, so the shape of it can no longer change.
    /// </summary>
    /// <remarks>
    /// Orders point at it and say what it took off them. Editing 20% into 50%
    /// after the fact would leave the reports disagreeing with the invoices.
    /// Pause it and write a new one.
    /// </remarks>
    public const string DiscountInUse = Prefix + "discount_in_use.conflict";
}
