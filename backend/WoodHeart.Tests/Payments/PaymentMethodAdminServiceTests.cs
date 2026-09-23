using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Payments;
using WoodHeart.Domain.Enums.Payments;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Payments;
using WoodHeart.Service.DTOs.Payments;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Payments;
using WoodHeart.Service.Services.Payments;

namespace WoodHeart.Tests.Payments;

/// <summary>
/// Configuring how the shop takes money.
/// </summary>
/// <remarks>
/// <para>
/// The tests that matter are the refusals, because every one of them is a way
/// to switch a method on and have a customer see nothing change. The resolver
/// drops a row nothing implements, a row offered in neither zone, and a
/// gateway with no credential to authenticate with — silently, and correctly.
/// This service is where that silence is turned into a sentence.
/// </para>
/// <para>
/// The other one with real money behind it is the credential: three states on
/// the way in, and conflating any two of them wipes a merchant key on the next
/// unrelated save.
/// </para>
/// </remarks>
public class PaymentMethodAdminServiceTests
{
    private readonly IPaymentMethodConfigRepository _configs =
        Substitute.For<IPaymentMethodConfigRepository>();

    private readonly ISecretProtector _secrets = Substitute.For<ISecretProtector>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    public PaymentMethodAdminServiceTests()
    {
        // Reversible and obvious, so a test can see what was stored without
        // the assertion turning into a second implementation of the cipher.
        _secrets.Protect(Arg.Any<string>()).Returns(call => $"enc:{call.Arg<string>()}");

        _secrets.TryUnprotect(Arg.Any<string>(), out Arg.Any<string?>())
            .Returns(call =>
            {
                var cipher = call.ArgAt<string>(0);
                var ok = cipher.StartsWith("enc:", StringComparison.Ordinal);

                call[1] = ok ? cipher[4..] : null;

                return ok;
            });

        _unitOfWork
            .ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<GeneralResponse<PaymentMethodConfigDto>>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
                call.Arg<Func<CancellationToken, Task<GeneralResponse<PaymentMethodConfigDto>>>>()(
                    CancellationToken.None));
    }

    // -------------------------------------------------------------------------
    // What the screen is told
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_method_nothing_implements_says_so()
    {
        // bKash has a configuration row from the seed and no class behind it
        // until Phase 5 writes one. The screen has to be able to say that,
        // because otherwise it shows a method the checkout will never offer.
        var service = Service(Config(code: PaymentMethodCodes.Bkash, enabled: false));

        var result = await service.GetAsync(PaymentMethodCodes.Bkash);

        result.IsSuccess.ShouldBeTrue();
        result.Data!.IsImplemented.ShouldBeFalse();
    }

    [Fact]
    public async Task A_stored_credential_is_reported_but_never_returned()
    {
        var service = Service(Config(credentials: "enc:app-key=live-1234"));

        var dto = (await service.GetAsync(PaymentMethodCodes.CashOnDelivery)).Data!;

        dto.HasCredentials.ShouldBeTrue();
        dto.CredentialsUnreadable.ShouldBeFalse();

        // Nothing on the shape can carry it. A screen that displays a live
        // merchant secret is one screenshot away from being a public one.
        typeof(PaymentMethodConfigDto)
            .GetProperties()
            .ShouldNotContain(p => p.Name == "Credentials");
    }

    [Fact]
    public async Task A_credential_that_no_longer_decrypts_is_flagged()
    {
        // A Data Protection key ring rotated with a container. The row looks
        // configured and cannot pay anybody, and the shop must learn it here
        // rather than from a customer at the moment of paying.
        var service = Service(Config(credentials: "written-under-a-key-that-is-gone"));

        var dto = (await service.GetAsync(PaymentMethodCodes.CashOnDelivery)).Data!;

        dto.HasCredentials.ShouldBeTrue();
        dto.CredentialsUnreadable.ShouldBeTrue();
    }

    // -------------------------------------------------------------------------
    // The refusals
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Enabling_a_method_nothing_implements_is_refused()
    {
        var service = Service(Config(code: PaymentMethodCodes.Bkash, enabled: false));

        var result = await service.UpdateAsync(
            PaymentMethodCodes.Bkash, Update(enabled: true));

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(PaymentErrors.NotImplemented);
    }

    [Fact]
    public async Task Enabling_a_gateway_with_no_credentials_is_refused()
    {
        var service = Service(
            Config(code: "gateway"),
            new FakeProvider("gateway", needsCredentials: true));

        var result = await service.UpdateAsync("gateway", Update(enabled: true));

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(PaymentErrors.CredentialsRequired);
    }

    [Fact]
    public async Task A_gateway_may_be_enabled_in_the_same_save_that_gives_it_credentials()
    {
        // The natural thing to do on the screen: paste the merchant key and
        // tick the box. Checking what is stored rather than what is arriving
        // would refuse it and leave the admin saving twice for no reason.
        var service = Service(
            Config(code: "gateway"),
            new FakeProvider("gateway", needsCredentials: true));

        var result = await service.UpdateAsync(
            "gateway", Update(enabled: true, credentials: "app-key=live-1234"));

        result.IsSuccess.ShouldBeTrue(result.Message);
        result.Data!.HasCredentials.ShouldBeTrue();
    }

    [Fact]
    public async Task Enabling_a_method_offered_in_neither_zone_is_refused()
    {
        // On, and invisible everywhere. That is off, spelled in a way nobody
        // would recognise three weeks later.
        var service = Service(Config());

        var result = await service.UpdateAsync(
            PaymentMethodCodes.CashOnDelivery,
            Update(enabled: true, insideDhaka: false, outsideDhaka: false));

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(PaymentErrors.NoZone);
    }

    [Fact]
    public async Task A_floor_above_the_ceiling_is_refused()
    {
        var service = Service(Config());

        var result = await service.UpdateAsync(
            PaymentMethodCodes.CashOnDelivery, Update(min: 100_000m, max: 50_000m));

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(PaymentErrors.AmountBandInvalid);
    }

    [Fact]
    public async Task A_percentage_charge_over_a_hundred_is_refused()
    {
        var service = Service(Config());

        var result = await service.UpdateAsync(
            PaymentMethodCodes.CashOnDelivery,
            Update(chargeType: PaymentChargeType.Percent, chargeValue: 150m));

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(PaymentErrors.ChargeInvalid);
    }

    [Fact]
    public async Task A_method_being_switched_off_is_not_held_to_any_of_it()
    {
        // Everything above is about a method customers will be offered. One
        // being turned off can be in any state at all — and refusing to let
        // somebody disable a misconfigured method would be the worst possible
        // moment to be strict.
        var service = Service(Config(code: PaymentMethodCodes.Bkash));

        var result = await service.UpdateAsync(
            PaymentMethodCodes.Bkash,
            Update(enabled: false, insideDhaka: false, outsideDhaka: false));

        result.IsSuccess.ShouldBeTrue(result.Message);
    }

    // -------------------------------------------------------------------------
    // The three states of a credential
    // -------------------------------------------------------------------------

    [Fact]
    public async Task An_absent_credential_leaves_the_stored_one_alone()
    {
        // The ordinary save: the screen has nothing to send back, because it
        // was never given the secret to redisplay. Treating that as "clear it"
        // would wipe a merchant key every time somebody renamed the method.
        var config = Config(credentials: "enc:app-key=live-1234");
        var service = Service(config);

        await service.UpdateAsync(PaymentMethodCodes.CashOnDelivery, Update(credentials: null));

        config.Credentials.ShouldBe("enc:app-key=live-1234");
    }

    [Fact]
    public async Task An_empty_credential_clears_it()
    {
        var config = Config(credentials: "enc:app-key=live-1234");
        var service = Service(config);

        await service.UpdateAsync(PaymentMethodCodes.CashOnDelivery, Update(credentials: ""));

        config.Credentials.ShouldBeNull();
    }

    [Fact]
    public async Task A_new_credential_is_encrypted_before_it_is_stored()
    {
        var config = Config();
        var service = Service(config);

        await service.UpdateAsync(
            PaymentMethodCodes.CashOnDelivery, Update(credentials: "app-key=live-1234"));

        // Through the protector on the way in, never stored as typed.
        _secrets.Received(1).Protect("app-key=live-1234");
        config.Credentials.ShouldBe("enc:app-key=live-1234");
    }

    // -------------------------------------------------------------------------
    // Saving
    // -------------------------------------------------------------------------

    [Fact]
    public async Task The_ceiling_that_keeps_a_rider_from_carrying_two_lakh_is_saved()
    {
        var config = Config();
        var service = Service(config);

        var result = await service.UpdateAsync(
            PaymentMethodCodes.CashOnDelivery, Update(max: 100_000m));

        result.IsSuccess.ShouldBeTrue(result.Message);
        config.MaxOrderAmount.ShouldBe(Money.Taka(100_000m));
        result.Data!.MaxOrderAmount.ShouldBe(100_000m);
    }

    [Fact]
    public async Task Choosing_no_charge_forgets_whatever_figure_was_there()
    {
        // Otherwise a method reads "no charge" beside a 50 that would come back
        // the moment somebody picked Fixed again, having meant to type 20.
        var config = Config(chargeType: PaymentChargeType.Fixed, chargeValue: 50m);
        var service = Service(config);

        await service.UpdateAsync(
            PaymentMethodCodes.CashOnDelivery,
            Update(chargeType: PaymentChargeType.None, chargeValue: 50m));

        config.ChargeValue.ShouldBe(0m);
    }

    [Fact]
    public async Task A_code_nobody_has_is_a_not_found_rather_than_a_new_method()
    {
        // There is no create, deliberately: a row invented from a screen would
        // read as configured and never appear at checkout.
        _configs.GetByCodeAsync("nagad", Arg.Any<CancellationToken>())
            .Returns((PaymentMethodConfig?)null);

        var service = new PaymentMethodAdminService(
            _configs, [new CodPaymentProvider()], _secrets, _unitOfWork,
            NullLogger<PaymentMethodAdminService>.Instance);

        var result = await service.UpdateAsync("nagad", Update());

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(PaymentErrors.MethodNotFound);
    }

    // -------------------------------------------------------------------------

    private PaymentMethodAdminService Service(
        PaymentMethodConfig config, params IPaymentProvider[] providers)
    {
        _configs.GetByCodeAsync(config.Code, Arg.Any<CancellationToken>()).Returns(config);
        _configs.GetAllOrderedAsync(Arg.Any<CancellationToken>()).Returns([config]);

        return new PaymentMethodAdminService(
            _configs,
            providers.Length > 0 ? providers : [new CodPaymentProvider()],
            _secrets,
            _unitOfWork,
            NullLogger<PaymentMethodAdminService>.Instance);
    }

    private static PaymentMethodConfig Config(
        string code = PaymentMethodCodes.CashOnDelivery,
        bool enabled = true,
        string? credentials = null,
        PaymentChargeType chargeType = PaymentChargeType.None,
        decimal chargeValue = 0m) =>
        new()
        {
            Id = 1,
            Code = code,
            DisplayName = LocalizedText.Create("Cash on delivery", "ক্যাশ অন ডেলিভারি"),
            IsEnabled = enabled,
            SortOrder = 10,
            Mode = PaymentMode.Live,
            AvailableInsideDhaka = true,
            AvailableOutsideDhaka = true,
            ChargeType = chargeType,
            ChargeValue = chargeValue,
            Credentials = credentials
        };

    private static UpdatePaymentMethodDto Update(
        bool enabled = true,
        bool insideDhaka = true,
        bool outsideDhaka = true,
        decimal? min = null,
        decimal? max = null,
        PaymentChargeType chargeType = PaymentChargeType.None,
        decimal chargeValue = 0m,
        string? credentials = null) =>
        new()
        {
            DisplayNameEn = "Cash on delivery",
            DisplayNameBn = "ক্যাশ অন ডেলিভারি",
            IsEnabled = enabled,
            SortOrder = 10,
            Mode = PaymentMode.Live,
            MinOrderAmount = min,
            MaxOrderAmount = max,
            AvailableInsideDhaka = insideDhaka,
            AvailableOutsideDhaka = outsideDhaka,
            ChargeType = chargeType,
            ChargeValue = chargeValue,
            Credentials = credentials
        };

    /// <summary>A provider that exists only to have capabilities.</summary>
    private sealed class FakeProvider(string code, bool needsCredentials) : IPaymentProvider
    {
        public string Code { get; } = code;

        public PaymentCapabilities Capabilities { get; } = new(
            SupportsRedirect: true,
            SupportsRefund: true,
            SupportsWebhook: true,
            SettlesImmediately: false,
            NeedsCredentials: needsCredentials);

        public Task<GeneralResponse<InitiateResult>> InitiateAsync(
            PaymentContext context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GeneralResponse<ExecuteResult>> ExecuteAsync(
            string reference, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GeneralResponse<PaymentStatusResult>> QueryAsync(
            string reference, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GeneralResponse<RefundResult>> RefundAsync(
            RefundRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
