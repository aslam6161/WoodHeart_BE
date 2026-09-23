namespace WoodHeart.Domain.Constants;

/// <summary>The payment method codes the application knows by name.</summary>
/// <remarks>
/// A code appears in exactly two places: a <c>PaymentMethodConfig</c> row and
/// the <c>IPaymentProvider</c> that implements it. This constant is what stops
/// the two being spelled differently — a typo there is a method that is
/// configured, looks enabled in the admin screen, and never appears at
/// checkout.
/// </remarks>
public static class PaymentMethodCodes
{
    public const string CashOnDelivery = "cod";

    /// <summary>Configured and built in Phase 5; disabled until the merchant account exists.</summary>
    public const string Bkash = "bkash";
}

/// <summary>
/// Stable error codes for payments.
/// </summary>
/// <remarks>
/// Same contract as the other error classes: the Angular client branches on the
/// code, the message is prose that will be translated, and the suffix picks the
/// HTTP status in <c>BaseApiController.HandleResult</c>.
/// </remarks>
public static class PaymentErrors
{
    private const string Prefix = "payments.";

    /// <summary>
    /// The chosen method is not available for this order.
    /// </summary>
    /// <remarks>
    /// One code for every reason — unknown, disabled, wrong zone, outside the
    /// amount band. Distinguishing them would let anyone probing the endpoint
    /// discover the shop's cash-on-delivery ceiling by binary search.
    /// </remarks>
    public const string MethodNotAvailable = Prefix + "method_not_available.conflict";

    /// <summary>An operation this provider does not implement — a cash refund, say.</summary>
    public const string OperationNotSupported = Prefix + "operation_not_supported.conflict";

    /// <summary>The gateway rejected the payment.</summary>
    public const string Declined = Prefix + "declined.conflict";

    /// <summary>The gateway could not be reached, or answered with something unusable.</summary>
    public const string GatewayUnavailable = "external.payment_gateway_unavailable";

    // --- Configuring one, from the admin screen -------------------------------

    public const string MethodNotFound = Prefix + "method.not_found";

    /// <summary>
    /// Enabling a method no code implements.
    /// </summary>
    /// <remarks>
    /// The resolver skips such a row in silence, so the shop would see the
    /// method switched on and customers would never be offered it. Said out
    /// loud at the moment of the toggle instead.
    /// </remarks>
    public const string NotImplemented = Prefix + "not_implemented.conflict";

    /// <summary>Enabling a gateway that needs a merchant credential and has none.</summary>
    public const string CredentialsRequired = Prefix + "credentials_required.conflict";

    /// <summary>Enabled, but offered in neither zone — which is off, spelled oddly.</summary>
    public const string NoZone = Prefix + "no_zone";

    /// <summary>A floor above the ceiling. No order can ever be between them.</summary>
    public const string AmountBandInvalid = Prefix + "amount_band_invalid";

    /// <summary>A charge that is not a charge: a percentage over 100, or a figure with no type.</summary>
    public const string ChargeInvalid = Prefix + "charge_invalid";
}
