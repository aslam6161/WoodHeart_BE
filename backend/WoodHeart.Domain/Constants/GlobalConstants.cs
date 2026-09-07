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
    public const string LowStockThreshold = "inventory.low_stock_threshold";

    public const string StorePhone = "store.phone";
    public const string StoreEmail = "store.email";
}

/// <summary>Feature flag names.</summary>
public static class FeatureFlags
{
    /// <summary>The switch that takes bKash live. Off until the merchant account is approved.</summary>
    public const string BkashEnabled = "bkash.enabled";

    public const string ConsultationsEnabled = "consultations.enabled";
    public const string ReviewsEnabled = "reviews.enabled";
}
