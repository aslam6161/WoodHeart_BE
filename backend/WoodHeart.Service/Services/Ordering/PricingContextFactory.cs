using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.Pricing;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Ordering;

namespace WoodHeart.Service.Services.Ordering;

/// <summary>
/// Reads the tax and delivery settings into the shape <c>CartPricer</c> wants.
/// </summary>
/// <remarks>
/// <para>
/// The whole reason this is a separate, tiny service: the cart page and order
/// placement must price identically, and the surest way to guarantee that is
/// for both to build their context here. A second copy of "read the VAT rate,
/// read the zone charge" is a second place to forget the free-delivery
/// threshold.
/// </para>
/// <para>
/// <b>Every fallback is zero, and that is deliberate.</b> The real figures are
/// open business questions (PLAN.md §16) and the seeded values are
/// placeholders. If a setting is missing or unparseable, charging nothing is
/// wrong in the shop's favour rather than the customer's — an under-charge is a
/// conversation, an over-charge is a refund and a complaint.
/// </para>
/// </remarks>
public class PricingContextFactory(IStoreSettingService settings) : IPricingContextFactory
{
    public async Task<PricingContext> BuildAsync(
        DeliveryZone? zone, CancellationToken cancellationToken = default)
    {
        var vatRate = await settings.GetDecimalAsync(SettingKeys.VatRate, 0m, cancellationToken);

        var includeVat = await settings.GetBoolAsync(
            SettingKeys.PricesIncludeVat, true, cancellationToken);

        var vatOnDelivery = await settings.GetBoolAsync(
            SettingKeys.VatOnDelivery, false, cancellationToken);

        var threshold = await settings.GetDecimalAsync(
            SettingKeys.FreeDeliveryThreshold, 0m, cancellationToken);

        // Null, not zero, when the customer has not said where they live. Zero
        // would read as "delivery is free" on the cart page, and the number
        // would then go up at checkout — which is exactly the surprise that
        // makes people abandon a basket.
        Money? zoneCharge = null;

        if (zone is { } chosen)
        {
            var key = chosen == DeliveryZone.InsideDhaka
                ? SettingKeys.DeliveryChargeInsideDhaka
                : SettingKeys.DeliveryChargeOutsideDhaka;

            zoneCharge = Money.Taka(await settings.GetDecimalAsync(key, 0m, cancellationToken));
        }

        return new PricingContext(
            VatRatePercent: vatRate,
            PricesIncludeVat: includeVat,
            ZoneDeliveryCharge: zoneCharge,
            FreeDeliveryThreshold: threshold > 0m ? Money.Taka(threshold) : null,
            VatOnDelivery: vatOnDelivery);
    }
}
