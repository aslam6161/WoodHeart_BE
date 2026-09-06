using NSubstitute;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Payments;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.Enums.Payments;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository.Interfaces.Payments;
using WoodHeart.Service.Interfaces.Payments;
using WoodHeart.Service.Services.Payments;

namespace WoodHeart.Tests.Payments;

/// <summary>
/// Which payment methods a given order may use.
/// </summary>
/// <remarks>
/// <para>
/// Three conditions have to hold together: a provider exists in code, the shop
/// has enabled the method, and it fits this order's amount and zone. The tests
/// that matter are the ones where two of the three hold and the method must
/// still not appear — a configuration row for a gateway nobody has written, and
/// a provider left in the code with its row switched off.
/// </para>
/// <para>
/// The other rule with money behind it is the cash-on-delivery ceiling, which
/// is how a shop says it will not send a rider out to collect 180,000৳ in
/// notes.
/// </para>
/// </remarks>
public class PaymentProviderResolverTests
{
    private readonly IPaymentMethodConfigRepository _configs =
        Substitute.For<IPaymentMethodConfigRepository>();

    private static PaymentMethodConfig Config(
        string code = PaymentMethodCodes.CashOnDelivery,
        bool enabled = true,
        decimal? min = null,
        decimal? max = null,
        bool insideDhaka = true,
        bool outsideDhaka = true,
        PaymentChargeType chargeType = PaymentChargeType.None,
        decimal chargeValue = 0m) =>
        new()
        {
            Code = code,
            DisplayName = LocalizedText.Create("Cash on delivery"),
            IsEnabled = enabled,
            MinOrderAmount = min is { } lo ? Money.Taka(lo) : null,
            MaxOrderAmount = max is { } hi ? Money.Taka(hi) : null,
            AvailableInsideDhaka = insideDhaka,
            AvailableOutsideDhaka = outsideDhaka,
            ChargeType = chargeType,
            ChargeValue = chargeValue
        };

    private PaymentProviderResolver Resolver(
        PaymentMethodConfig config, params IPaymentProvider[] providers)
    {
        _configs.GetEnabledAsync(Arg.Any<CancellationToken>())
            .Returns(config.IsEnabled ? [config] : Array.Empty<PaymentMethodConfig>());

        _configs.GetByCodeAsync(config.Code, Arg.Any<CancellationToken>()).Returns(config);

        return new PaymentProviderResolver(
            _configs, providers.Length > 0 ? providers : [new CodPaymentProvider()]);
    }

    private static Money Total(decimal amount = 50_000m) => Money.Taka(amount);

    // -------------------------------------------------------------------------
    // The three conditions
    // -------------------------------------------------------------------------

    [Fact]
    public async Task An_enabled_method_with_a_provider_is_offered()
    {
        var eligible = await Resolver(Config())
            .GetEligibleAsync(Total(), DeliveryZone.InsideDhaka);

        eligible.Count.ShouldBe(1);
        eligible[0].Config.Code.ShouldBe(PaymentMethodCodes.CashOnDelivery);
    }

    [Fact]
    public async Task A_disabled_method_is_not_offered()
    {
        var eligible = await Resolver(Config(enabled: false))
            .GetEligibleAsync(Total(), DeliveryZone.InsideDhaka);

        eligible.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_configured_method_with_no_provider_behind_it_is_not_offered()
    {
        // Someone enables bKash in the admin screen before the provider exists.
        // It must not appear at checkout, because choosing it would take a
        // customer to a payment flow that has not been written.
        var eligible = await Resolver(Config(code: PaymentMethodCodes.Bkash))
            .GetEligibleAsync(Total(), DeliveryZone.InsideDhaka);

        eligible.ShouldBeEmpty();
    }

    [Fact]
    public async Task Resolving_a_disabled_method_by_code_returns_nothing()
    {
        // The placement path, not the display path. A client that remembers the
        // code from before the shop switched it off must not be able to use it.
        var resolved = await Resolver(Config(enabled: false))
            .ResolveAsync(PaymentMethodCodes.CashOnDelivery, Total(), DeliveryZone.InsideDhaka);

        resolved.ShouldBeNull();
    }

    [Fact]
    public async Task Resolving_an_unknown_code_returns_nothing()
    {
        var resolved = await Resolver(Config())
            .ResolveAsync("nagad", Total(), DeliveryZone.InsideDhaka);

        resolved.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // Amount bands — the cash-on-delivery ceiling
    // -------------------------------------------------------------------------

    [Fact]
    public async Task An_order_above_the_ceiling_cannot_use_the_method()
    {
        // "We will not send a rider to collect 180,000৳ in cash."
        var eligible = await Resolver(Config(max: 100_000m))
            .GetEligibleAsync(Total(180_000m), DeliveryZone.InsideDhaka);

        eligible.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_order_exactly_at_the_ceiling_is_still_allowed()
    {
        // A ceiling of 100,000 that refuses exactly 100,000 is an off-by-one a
        // shop discovers through a complaint rather than a test.
        var eligible = await Resolver(Config(max: 100_000m))
            .GetEligibleAsync(Total(100_000m), DeliveryZone.InsideDhaka);

        eligible.Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_order_below_the_floor_cannot_use_the_method()
    {
        var eligible = await Resolver(Config(min: 500m))
            .GetEligibleAsync(Total(499m), DeliveryZone.InsideDhaka);

        eligible.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_order_exactly_at_the_floor_is_allowed()
    {
        var eligible = await Resolver(Config(min: 500m))
            .GetEligibleAsync(Total(500m), DeliveryZone.InsideDhaka);

        eligible.Count.ShouldBe(1);
    }

    // -------------------------------------------------------------------------
    // Zone
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_method_switched_off_outside_Dhaka_is_only_offered_inside_it()
    {
        var config = Config(outsideDhaka: false);

        (await Resolver(config).GetEligibleAsync(Total(), DeliveryZone.InsideDhaka))
            .Count.ShouldBe(1);

        (await Resolver(config).GetEligibleAsync(Total(), DeliveryZone.OutsideDhaka))
            .ShouldBeEmpty();
    }

    // -------------------------------------------------------------------------
    // Surcharge
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_method_with_no_charge_adds_nothing()
    {
        var eligible = await Resolver(Config())
            .GetEligibleAsync(Total(), DeliveryZone.InsideDhaka);

        eligible[0].Surcharge.Amount.ShouldBe(0m);
    }

    [Fact]
    public async Task A_fixed_charge_is_added_as_it_stands()
    {
        var eligible = await Resolver(Config(chargeType: PaymentChargeType.Fixed, chargeValue: 50m))
            .GetEligibleAsync(Total(), DeliveryZone.InsideDhaka);

        eligible[0].Surcharge.Amount.ShouldBe(50m);
    }

    [Fact]
    public async Task A_percentage_charge_is_rounded_to_whole_taka()
    {
        // 1.5% of 2,497৳ is 37.455৳, which cannot be collected at a doorstep.
        var eligible = await Resolver(Config(chargeType: PaymentChargeType.Percent, chargeValue: 1.5m))
            .GetEligibleAsync(Total(2497m), DeliveryZone.InsideDhaka);

        eligible[0].Surcharge.Amount.ShouldBe(37m);
    }

    [Fact]
    public async Task A_negative_charge_never_pays_the_customer()
    {
        var eligible = await Resolver(Config(chargeType: PaymentChargeType.Fixed, chargeValue: -50m))
            .GetEligibleAsync(Total(), DeliveryZone.InsideDhaka);

        eligible[0].Surcharge.Amount.ShouldBe(0m);
    }
}
