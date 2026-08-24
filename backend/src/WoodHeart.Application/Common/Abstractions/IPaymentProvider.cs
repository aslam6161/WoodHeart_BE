using WoodHeart.Domain.Common;

namespace WoodHeart.Application.Common.Abstractions;

/// <summary>
/// One way to take money. Cash on delivery, bKash, and later Nagad or
/// SSLCOMMERZ all implement this same port.
/// </summary>
/// <remarks>
/// <para>
/// The registry pattern behind this interface is what makes the admin
/// requirement — "we only support cash on delivery now, but I want to turn on
/// bKash myself later" — a database toggle rather than a deployment. A provider
/// is available to a customer only when it is registered in DI <em>and</em>
/// enabled in <c>PaymentMethodConfig</c> <em>and</em> eligible for that
/// particular cart.
/// </para>
/// <para>
/// Every implementation must be idempotent on <see cref="ExecuteAsync"/>:
/// mobile networks here drop callbacks routinely, and the reconciliation job
/// will retry. Charging a customer twice is the one unrecoverable bug in this
/// system.
/// </para>
/// </remarks>
public interface IPaymentProvider
{
    /// <summary>Stable code matching <c>PaymentMethodConfig.Code</c> — <c>cod</c>, <c>bkash</c>.</summary>
    string Code { get; }

    PaymentCapabilities Capabilities { get; }

    /// <summary>
    /// Starts a payment. For COD this simply confirms; for bKash it creates the
    /// payment and returns the URL to redirect the customer to.
    /// </summary>
    Task<Result<PaymentInitiation>> InitiateAsync(
        PaymentContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes a payment after the customer returns from the gateway.
    /// Must be safe to call more than once with the same reference.
    /// </summary>
    Task<Result<PaymentOutcome>> ExecuteAsync(
        string providerReference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the gateway what actually happened. This is the answer to
    /// "the customer says they paid but we never got the callback".
    /// </summary>
    Task<Result<PaymentOutcome>> QueryAsync(
        string providerReference,
        CancellationToken cancellationToken = default);

    Task<Result<RefundOutcome>> RefundAsync(
        RefundRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>What a provider can actually do, so the UI never offers an impossible action.</summary>
public sealed record PaymentCapabilities
{
    /// <summary>The customer leaves our site and comes back (bKash) vs. stays (COD).</summary>
    public bool RequiresRedirect { get; init; }

    public bool SupportsRefund { get; init; }

    public bool SupportsPartialRefund { get; init; }

    /// <summary>Money arrives before dispatch (bKash) rather than after (COD).</summary>
    public bool IsPrepaid { get; init; }

    public bool SupportsWebhook { get; init; }
}

/// <summary>Everything a provider needs to start taking a payment.</summary>
public sealed record PaymentContext
{
    public required Guid OrderId { get; init; }

    public required string OrderNumber { get; init; }

    public required Money Amount { get; init; }

    public required PhoneNumber CustomerPhone { get; init; }

    public string? CustomerEmail { get; init; }

    /// <summary>Deduplicates retries of the same logical attempt at the gateway.</summary>
    public required string IdempotencyKey { get; init; }

    /// <summary>Where the gateway sends the customer back to.</summary>
    public string? ReturnUrl { get; init; }

    public string? CancelUrl { get; init; }
}

public sealed record PaymentInitiation
{
    public required PaymentState State { get; init; }

    /// <summary>The gateway's own id for this payment — persist it, always.</summary>
    public required string ProviderReference { get; init; }

    /// <summary>Where to send the customer, when <see cref="PaymentCapabilities.RequiresRedirect"/>.</summary>
    public string? RedirectUrl { get; init; }

    /// <summary>Raw gateway response, stored for dispute resolution.</summary>
    public string? RawResponse { get; init; }
}

public sealed record PaymentOutcome
{
    public required PaymentState State { get; init; }

    public required string ProviderReference { get; init; }

    /// <summary>The customer-quotable transaction id, e.g. a bKash trxID.</summary>
    public string? TransactionId { get; init; }

    public Money? AmountPaid { get; init; }

    public string? CustomerAccount { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    public string? FailureReason { get; init; }

    public string? RawResponse { get; init; }
}

public sealed record RefundRequest
{
    public required string ProviderReference { get; init; }

    public required string TransactionId { get; init; }

    public required Money Amount { get; init; }

    public required string Reason { get; init; }

    public required string IdempotencyKey { get; init; }
}

public sealed record RefundOutcome
{
    public required bool Succeeded { get; init; }

    public string? RefundTransactionId { get; init; }

    public Money? AmountRefunded { get; init; }

    public string? FailureReason { get; init; }

    public string? RawResponse { get; init; }
}

/// <summary>Where a payment attempt currently stands at the provider.</summary>
public enum PaymentState
{
    /// <summary>Created at the gateway; waiting for the customer to act.</summary>
    Pending = 0,

    /// <summary>Money confirmed received.</summary>
    Succeeded = 1,

    /// <summary>The gateway rejected it or the customer abandoned it.</summary>
    Failed = 2,

    /// <summary>The customer cancelled deliberately.</summary>
    Cancelled = 3,

    /// <summary>
    /// Accepted, but money is not in hand yet — cash on delivery lives here for
    /// its entire life until the rider collects.
    /// </summary>
    AwaitingCollection = 4
}

/// <summary>
/// Picks the providers a given cart may actually use.
/// </summary>
public interface IPaymentProviderResolver
{
    /// <summary>Providers enabled in configuration and eligible for this order value and zone.</summary>
    Task<IReadOnlyList<AvailablePaymentMethod>> GetAvailableAsync(
        PaymentEligibilityContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Resolves one provider by code, or a NotFound error if it is disabled or unknown.</summary>
    Task<Result<IPaymentProvider>> ResolveAsync(string code, CancellationToken cancellationToken = default);
}

public sealed record PaymentEligibilityContext
{
    public required Money OrderTotal { get; init; }

    public string? DeliveryZone { get; init; }

    public Guid? CustomerGroupId { get; init; }

    /// <summary>Made-to-order carts may be restricted to prepaid methods only.</summary>
    public bool ContainsMadeToOrder { get; init; }
}

public sealed record AvailablePaymentMethod
{
    public required string Code { get; init; }

    public required LocalizedText DisplayName { get; init; }

    public LocalizedText? Description { get; init; }

    public string? IconUrl { get; init; }

    public required bool RequiresRedirect { get; init; }

    /// <summary>Surcharge for choosing this method, already computed for this cart.</summary>
    public Money? ExtraCharge { get; init; }

    /// <summary>Advance the customer must pay now even under cash on delivery.</summary>
    public Money? RequiredAdvance { get; init; }

    public int SortOrder { get; init; }
}
