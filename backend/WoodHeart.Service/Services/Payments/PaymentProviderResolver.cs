using WoodHeart.Domain.Entity.Payments;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.Enums.Payments;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository.Interfaces.Payments;
using WoodHeart.Service.Interfaces.Payments;

namespace WoodHeart.Service.Services.Payments;

/// <inheritdoc />
public class PaymentProviderResolver(
    IPaymentMethodConfigRepository configs,
    IEnumerable<IPaymentProvider> providers) : IPaymentProviderResolver
{
    /// <summary>
    /// The providers that exist in code, by their code.
    /// </summary>
    /// <remarks>
    /// Built once per scope from whatever DI holds. A configuration row whose
    /// code matches nothing here is simply never offered — so an admin cannot
    /// enable a gateway nobody has written, and a half-finished provider cannot
    /// start taking money because somebody left a row enabled.
    /// </remarks>
    private readonly Dictionary<string, IPaymentProvider> _providers =
        providers.ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<EligiblePaymentMethod>> GetEligibleAsync(
        Money orderTotal, DeliveryZone zone, CancellationToken cancellationToken = default)
    {
        var enabled = await configs.GetEnabledAsync(cancellationToken);

        return
        [
            .. enabled
                .Where(config => IsEligible(config, orderTotal, zone))
                .Where(config => _providers.ContainsKey(config.Code))
                .Select(config => new EligiblePaymentMethod(
                    config, _providers[config.Code], SurchargeFor(config, orderTotal)))
        ];
    }

    public async Task<EligiblePaymentMethod?> ResolveAsync(
        string code,
        Money orderTotal,
        DeliveryZone zone,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var config = await configs.GetByCodeAsync(code, cancellationToken);

        // Every failure returns the same null. Which of the four conditions
        // failed is not the caller's business, and telling them would let the
        // shop's cash-on-delivery ceiling be found by binary search.
        if (config is null
            || !config.IsEnabled
            || !IsEligible(config, orderTotal, zone)
            || !_providers.TryGetValue(config.Code, out var provider))
        {
            return null;
        }

        return new EligiblePaymentMethod(config, provider, SurchargeFor(config, orderTotal));
    }

    private static bool IsEligible(PaymentMethodConfig config, Money total, DeliveryZone zone)
    {
        if (!config.IsEnabled)
        {
            return false;
        }

        var zoneAllowed = zone == DeliveryZone.InsideDhaka
            ? config.AvailableInsideDhaka
            : config.AvailableOutsideDhaka;

        if (!zoneAllowed)
        {
            return false;
        }

        // Inclusive at both ends. A ceiling of 100,000৳ that refuses an order of
        // exactly 100,000৳ is the kind of off-by-one a shop discovers through a
        // customer complaint rather than a test.
        if (config.MinOrderAmount is { } min && total < min)
        {
            return false;
        }

        return config.MaxOrderAmount is not { } max || total <= max;
    }

    /// <summary>
    /// What this method adds to the bill.
    /// </summary>
    /// <remarks>
    /// Rounded to whole taka. A cash-handling charge of 37.46৳ cannot be
    /// collected at a doorstep, and the rounding belongs here rather than in the
    /// display, so that what the customer is shown is what they are asked for.
    /// </remarks>
    private static Money SurchargeFor(PaymentMethodConfig config, Money total) =>
        config.ChargeType switch
        {
            PaymentChargeType.Fixed =>
                Money.From(config.ChargeValue, total.Currency).OrZeroIfNegative().RoundToWholeTaka(),

            PaymentChargeType.Percent =>
                total.Percentage(config.ChargeValue).OrZeroIfNegative().RoundToWholeTaka(),

            _ => Money.Zero(total.Currency)
        };
}
