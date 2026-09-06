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
}
