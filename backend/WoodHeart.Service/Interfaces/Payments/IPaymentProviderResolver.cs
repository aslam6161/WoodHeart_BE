using WoodHeart.Domain.Entity.Payments;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Service.Interfaces.Payments;

/// <summary>One method, paired with the configuration that makes it available.</summary>
public readonly record struct EligiblePaymentMethod(
    PaymentMethodConfig Config,
    IPaymentProvider Provider,
    Money Surcharge);

/// <summary>
/// Which payment methods this particular order may use.
/// </summary>
/// <remarks>
/// <para>
/// Three conditions, all of which must hold: the provider is registered in DI,
/// the method is enabled in <see cref="PaymentMethodConfig"/>, and it is
/// eligible for <i>this</i> order — its amount band and its delivery zone.
/// </para>
/// <para>
/// <b>The same resolver answers both questions.</b> The checkout page asks
/// "what may I show?" and placement asks "may this one be used?", and they call
/// the same code. Two implementations would eventually disagree, and the shape
/// of that disagreement is a customer picking a method the server then refuses
/// — at the last step, having already typed their address.
/// </para>
/// </remarks>
public interface IPaymentProviderResolver
{
    /// <summary>Everything this order may pay with, in the shop's display order.</summary>
    Task<IReadOnlyList<EligiblePaymentMethod>> GetEligibleAsync(
        Money orderTotal, DeliveryZone zone, CancellationToken cancellationToken = default);

    /// <summary>
    /// One method by code, or null when it is not eligible for this order.
    /// </summary>
    /// <remarks>
    /// Null covers every reason at once — unknown code, disabled method, wrong
    /// zone, amount outside the band — and deliberately so. Telling a caller
    /// which of those it was tells anyone probing the endpoint what the shop's
    /// COD ceiling is.
    /// </remarks>
    Task<EligiblePaymentMethod?> ResolveAsync(
        string code, Money orderTotal, DeliveryZone zone, CancellationToken cancellationToken = default);
}
