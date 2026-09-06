using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Enums.Payments;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;

namespace WoodHeart.Service.Interfaces.Payments;

/// <summary>
/// What a payment method can actually do.
/// </summary>
/// <remarks>
/// Read by the checkout and by the admin screens so neither has to know the
/// name of any particular gateway. Cash on delivery has no redirect and no
/// automated refund; bKash has both. Branching on
/// <c>code == "cod"</c> instead would put a gateway's name into three layers.
/// </remarks>
/// <param name="SupportsRedirect">
/// The customer leaves the site and comes back — so placement returns a URL
/// rather than a finished order.
/// </param>
/// <param name="SupportsRefund">Money can be sent back through the gateway itself.</param>
/// <param name="SupportsWebhook">The gateway calls us, so a callback route must exist.</param>
/// <param name="SettlesImmediately">
/// The order can be confirmed the moment it is placed. True for cash on
/// delivery, which settles at the door rather than at the gateway.
/// </param>
public readonly record struct PaymentCapabilities(
    bool SupportsRedirect,
    bool SupportsRefund,
    bool SupportsWebhook,
    bool SettlesImmediately);

/// <summary>Everything a provider needs to start taking money for one order.</summary>
/// <param name="Order">The placed order, already written and numbered.</param>
/// <param name="Amount">What to collect. Includes any payment surcharge.</param>
/// <param name="ReturnUrl">Where the gateway sends the customer back to.</param>
/// <param name="IdempotencyKey">
/// Unique per attempt, so a retried request never charges twice.
/// </param>
public readonly record struct PaymentContext(
    Order Order,
    Money Amount,
    string? ReturnUrl,
    string IdempotencyKey);

/// <summary>What came back from starting a payment.</summary>
/// <param name="State">Where the money stands now.</param>
/// <param name="Reference">
/// The gateway's id for this payment, stored so reconciliation has something to
/// ask about later.
/// </param>
/// <param name="RedirectUrl">Where to send the customer, when there is anywhere.</param>
public readonly record struct InitiateResult(
    PaymentState State,
    string? Reference,
    string? RedirectUrl);

/// <summary>The result of completing a payment the customer has returned from.</summary>
public readonly record struct ExecuteResult(
    PaymentState State,
    string? Reference,
    Money? AmountCollected);

/// <summary>What the gateway says about a payment when asked.</summary>
/// <remarks>
/// This is what the reconciliation job calls, for the case that costs a shop
/// real money: the customer paid, the callback never arrived, and nobody knows
/// until they phone up asking where their sofa is.
/// </remarks>
public readonly record struct PaymentStatusResult(
    PaymentState State,
    Money? AmountCollected);

public readonly record struct RefundRequest(
    string Reference,
    Money Amount,
    string Reason,
    string IdempotencyKey);

public readonly record struct RefundResult(
    PaymentState State,
    string? RefundReference,
    Money? AmountRefunded);

/// <summary>
/// One way of taking money. Implemented per gateway.
/// </summary>
/// <remarks>
/// <para>
/// The port exists so that adding bKash is adding a class, not editing
/// checkout. Everything above this line — the cart, the order, the confirmation
/// — is written against these four methods and against
/// <see cref="Capabilities"/>, and none of it names a gateway.
/// </para>
/// <para>
/// Failures come back as a failed <see cref="GeneralResponse{T}"/> rather than
/// as exceptions, because "the gateway declined this card" is an ordinary
/// business outcome that the checkout page has to render. Exceptions stay
/// reserved for a gateway being unreachable, which is a different problem with
/// a different response.
/// </para>
/// </remarks>
public interface IPaymentProvider
{
    /// <summary>Matches <c>PaymentMethodConfig.Code</c>. This is the join between them.</summary>
    string Code { get; }

    PaymentCapabilities Capabilities { get; }

    /// <summary>Starts taking payment for an order that has already been written.</summary>
    Task<GeneralResponse<InitiateResult>> InitiateAsync(
        PaymentContext context, CancellationToken cancellationToken = default);

    /// <summary>Completes a payment the customer has come back from.</summary>
    Task<GeneralResponse<ExecuteResult>> ExecuteAsync(
        string reference, CancellationToken cancellationToken = default);

    /// <summary>Asks the gateway what it thinks the state is. Used by reconciliation.</summary>
    Task<GeneralResponse<PaymentStatusResult>> QueryAsync(
        string reference, CancellationToken cancellationToken = default);

    Task<GeneralResponse<RefundResult>> RefundAsync(
        RefundRequest request, CancellationToken cancellationToken = default);
}
