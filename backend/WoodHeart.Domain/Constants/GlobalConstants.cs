namespace WoodHeart.Domain.Constants;

/// <summary>Values that appear in more than one layer and must not drift.</summary>
public static class GlobalConstants
{
    /// <summary>The only currency v1 sells in.</summary>
    public const string Currency = "BDT";

    /// <summary>IANA id. Bangladesh is UTC+06 year round — no daylight saving.</summary>
    public const string TimeZoneId = "Asia/Dhaka";

    /// <summary>Windows' name for the same zone, needed when running outside a container.</summary>
    public const string WindowsTimeZoneId = "Bangladesh Standard Time";

    public const string DefaultLanguage = "en";
    public const string BanglaLanguage = "bn";

    public static readonly IReadOnlyList<string> SupportedLanguages = [DefaultLanguage, BanglaLanguage];

    public const string CorrelationIdHeader = "X-Correlation-Id";

    /// <summary>Identifies a guest's cart before they have signed in.</summary>
    public const string AnonymousIdHeader = "X-Anonymous-Id";

    public const string AnonymousIdCookie = "wh_anon";

    /// <summary>
    /// What an order number starts with, when the shop has not said otherwise.
    /// </summary>
    /// <remarks>
    /// The prefix is a store setting so a second brand could use the same
    /// installation; this is the fallback, and the reason a missing setting
    /// produces <c>WH-2609-00042</c> rather than <c>-2609-00042</c>.
    /// </remarks>
    public const string DefaultOrderNumberPrefix = "WH";

    /// <summary>
    /// What a booking number starts with: <c>WHC-2609-00003</c>.
    /// </summary>
    /// <remarks>
    /// Different from an order's on purpose. Both get read down a telephone,
    /// and a shop that hears "double-you aitch dash two six" has to ask which
    /// kind before it can look anything up.
    /// </remarks>
    public const string DefaultBookingNumberPrefix = "WHC";

    /// <summary>Quotations: <c>WHQ-2609-00042</c>.</summary>
    public const string DefaultQuotationNumberPrefix = "WHQ";

    /// <summary>The idempotency header checkout reads to make a retry safe.</summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";
}

/// <summary>Store setting keys. Typed access lives in <c>IStoreSettingService</c>.</summary>
public static class SettingKeys
{
    public const string VatRate = "tax.vat_rate";
    public const string PricesIncludeVat = "tax.prices_include_vat";

    /// <summary>
    /// Whether the delivery charge is itself taxable.
    /// </summary>
    /// <remarks>
    /// A setting rather than a constant because it is a business question
    /// (PLAN.md §16.1) and the answer differs by how the shop is registered.
    /// Defaults to false, which is the safer wrong answer: it under-charges
    /// rather than over-charges while the real answer is pending.
    /// </remarks>
    public const string VatOnDelivery = "tax.vat_on_delivery";

    /// <summary>
    /// The ordinary inside-Dhaka rate, used for any product that has not been
    /// costed individually.
    /// </summary>
    /// <remarks>
    /// A fallback, not the price. Delivery is priced per product — see
    /// <c>DeliveryPricer</c> — and this is what a product with a blank charge
    /// quotes so that a forgotten field is never a giveaway.
    /// </remarks>
    public const string DeliveryChargeInsideDhaka = "delivery.charge_inside_dhaka";

    /// <summary>The same fallback, for everywhere outside Dhaka.</summary>
    public const string DeliveryChargeOutsideDhaka = "delivery.charge_outside_dhaka";

    /// <summary>
    /// Goods total at or above which delivery is free. Zero disables it.
    /// </summary>
    /// <remarks>
    /// <b>Worth thinking about before switching on.</b> Now that delivery is
    /// priced per product, this waives the whole charge — including a
    /// wardrobe's carriage to Sylhet, which is the most expensive thing the
    /// shop moves.
    /// </remarks>
    public const string FreeDeliveryThreshold = "delivery.free_threshold";

    public const string OrderNumberPrefix = "orders.number_prefix";

    /// <summary>
    /// Minutes an order may sit unpaid at a gateway before it is cancelled
    /// and its stock released. Zero switches the expiry off.
    /// </summary>
    /// <remarks>
    /// Cash on delivery is never touched by this: a COD order is confirmed at
    /// placement, and the only orders still Pending are the ones whose
    /// customer was sent to pay and did not come back.
    /// </remarks>
    public const string UnpaidOrderExpiryMinutes = "orders.unpaid_expiry_minutes";

    /// <summary>
    /// Hours a basket may sit untouched before its owner is reminded it is
    /// still there. Zero switches the reminder off.
    /// </summary>
    /// <remarks>
    /// Not the same clock as the basket's own lifetime, which is thirty days.
    /// A basket is worth a reminder within the evening and worth keeping for a
    /// month, and one number cannot be both.
    /// </remarks>
    public const string CartRecoveryAfterHours = "carts.recovery_after_hours";

    public const string LowStockThreshold = "inventory.low_stock_threshold";

    /// <summary>Whether the morning low-stock message to the shop is sent at all.</summary>
    public const string LowStockDigest = "inventory.low_stock_digest";

    public const string StorePhone = "store.phone";
    public const string StoreEmail = "store.email";

    /// <summary>The trading name printed at the top of an invoice.</summary>
    /// <remarks>
    /// A setting rather than a constant because it is the one string that has
    /// to match the shop's paperwork exactly, and correcting it should not be a
    /// deployment.
    /// </remarks>
    public const string StoreName = "store.name";

    /// <summary>The shop's own address, as it should appear on an invoice.</summary>
    public const string StoreAddress = "store.address";

    /// <summary>
    /// The Business Identification Number issued by the NBR.
    /// </summary>
    /// <remarks>
    /// <b>Printed only when it is set.</b> An invoice that shows "BIN: —" is
    /// worse than one that shows no BIN line at all: the first looks like a
    /// registered business that failed to fill the field in, and a VAT
    /// registration number is a claim worth making only when it is true.
    /// </remarks>
    public const string StoreBin = "store.bin";
}

/// <summary>Feature flag names.</summary>
/// <summary>
/// Switches the shop can throw that change what the storefront offers.
/// </summary>
/// <remarks>
/// <para>
/// <b>A flag belongs here only when something reads it.</b> This class began
/// with three and two of them were read by nothing at all — a switch that does
/// not switch anything is worse than no switch, because the next person to
/// need one wires it up beside the real gate and the shop ends up with two
/// answers to the same question.
/// </para>
/// <para>
/// <c>bkash.enabled</c> was one of them. Whether bKash is offered is
/// <see cref="WoodHeart.Domain.Entity.Payments.PaymentMethodConfig.IsEnabled"/>
/// — a per-method switch on a screen the shop already uses, and one the
/// resolver backs up by refusing any code with no provider registered in DI.
/// A second switch for the same decision could only ever disagree with it.
/// </para>
/// <para>
/// <c>reviews.enabled</c> was the other. Reviews are Phase 6 and do not exist,
/// and a flag seeded years before its feature is a flag somebody forgets to
/// wire. It comes back with them.
/// </para>
/// </remarks>
public static class FeatureFlags
{
    /// <summary>
    /// Whether the shop is taking consultation bookings at all.
    /// </summary>
    /// <remarks>
    /// The shop has one consultant. Travel, illness, or a diary already full
    /// for a month are all reasons to stop taking bookings without deleting the
    /// services and the rules behind them — and without a shop owner having to
    /// explain to somebody who booked an afternoon that nobody is coming.
    /// Existing bookings are untouched; staff can still manage every one of
    /// them, because turning off new work must not lock the shop out of the
    /// work it already has.
    /// </remarks>
    public const string ConsultationsEnabled = "consultations.enabled";
}
