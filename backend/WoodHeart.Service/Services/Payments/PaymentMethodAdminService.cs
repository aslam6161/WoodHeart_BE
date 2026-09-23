using Microsoft.Extensions.Logging;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Payments;
using WoodHeart.Domain.Enums.Payments;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Payments;
using WoodHeart.Service.DTOs.Payments;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Payments;

namespace WoodHeart.Service.Services.Payments;

/// <inheritdoc cref="IPaymentMethodAdminService" />
/// <remarks>
/// <para>
/// <b>Three refusals, and each exists because the alternative is silent.</b>
/// The resolver drops a method that no class implements, that is offered in
/// neither zone, or whose gateway has no credential to authenticate with — it
/// simply never appears at checkout. An admin who toggled such a method on
/// would see it on, and customers would see nothing, with nowhere to look. So
/// they are refused here, at the moment of the toggle, with the reason.
/// </para>
/// <para>
/// <b>The credential is write-only in both directions.</b> It goes in
/// encrypted, it is never read back out, and the screen learns only whether
/// something is there — and whether it can still be decrypted, which is a
/// different question and a more urgent one.
/// </para>
/// </remarks>
public class PaymentMethodAdminService(
    IPaymentMethodConfigRepository configs,
    IEnumerable<IPaymentProvider> providers,
    ISecretProtector secrets,
    IUnitOfWork unitOfWork,
    ILogger<PaymentMethodAdminService> logger) : IPaymentMethodAdminService
{
    /// <summary>
    /// What is actually implemented, by code.
    /// </summary>
    /// <remarks>
    /// The same dictionary the resolver builds, and for the same reason: a
    /// configuration row is only half of a payment method.
    /// </remarks>
    private readonly Dictionary<string, IPaymentProvider> _providers =
        providers.ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);

    public async Task<GeneralResponse<IReadOnlyList<PaymentMethodConfigDto>>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var methods = await configs.GetAllOrderedAsync(cancellationToken);

        return GeneralResponse<IReadOnlyList<PaymentMethodConfigDto>>.Success(
            [.. methods.Select(ToDto)]);
    }

    public async Task<GeneralResponse<PaymentMethodConfigDto>> GetAsync(
        string code, CancellationToken cancellationToken = default)
    {
        var config = await configs.GetByCodeAsync(code ?? string.Empty, cancellationToken);

        return config is null
            ? GeneralResponse<PaymentMethodConfigDto>.Fail(
                PaymentErrors.MethodNotFound, "We do not have a payment method with that code.")
            : GeneralResponse<PaymentMethodConfigDto>.Success(ToDto(config));
    }

    public async Task<GeneralResponse<PaymentMethodConfigDto>> UpdateAsync(
        string code, UpdatePaymentMethodDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var config = await configs.GetByCodeAsync(code ?? string.Empty, cancellationToken);

        if (config is null)
        {
            return GeneralResponse<PaymentMethodConfigDto>.Fail(
                PaymentErrors.MethodNotFound, "We do not have a payment method with that code.");
        }

        // What the credential will be once this save lands, which is what the
        // "enabled with nothing to authenticate with" check has to reason
        // about — not what is stored now.
        var credentials = Resolve(dto.Credentials, config.Credentials);

        if (Refuse(dto, config.Code, credentials) is { } refusal)
        {
            return refusal;
        }

        return await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var wasEnabled = config.IsEnabled;

            config.DisplayName = LocalizedText.Create(dto.DisplayNameEn.Trim(), dto.DisplayNameBn);
            config.Description = string.IsNullOrWhiteSpace(dto.DescriptionEn)
                ? null
                : LocalizedText.Create(dto.DescriptionEn.Trim(), dto.DescriptionBn);
            config.IconUrl = Blank(dto.IconUrl);
            config.IsEnabled = dto.IsEnabled;
            config.SortOrder = dto.SortOrder;
            config.Mode = dto.Mode;
            config.MinOrderAmount = dto.MinOrderAmount is { } min ? Money.Taka(min) : null;
            config.MaxOrderAmount = dto.MaxOrderAmount is { } max ? Money.Taka(max) : null;
            config.AvailableInsideDhaka = dto.AvailableInsideDhaka;
            config.AvailableOutsideDhaka = dto.AvailableOutsideDhaka;
            config.ChargeType = dto.ChargeType;
            config.ChargeValue = dto.ChargeType == PaymentChargeType.None ? 0m : dto.ChargeValue;
            config.Credentials = credentials;

            configs.Update(config);
            await unitOfWork.SaveChangesAsync(ct);

            // Worth a line in the log on its own: turning a method on or off,
            // or moving it to live money, is the kind of change somebody asks
            // about three weeks later.
            if (wasEnabled != config.IsEnabled || config.Mode == PaymentMode.Live)
            {
                PaymentAdminLog.MethodConfigured(
                    logger, config.Code, config.IsEnabled, config.Mode.ToString());
            }

            return GeneralResponse<PaymentMethodConfigDto>.Success(ToDto(config));
        }, cancellationToken);
    }

    /// <summary>
    /// The three ways to switch a method on and have nothing happen, plus the
    /// two ways to describe a charge that is not one.
    /// </summary>
    private GeneralResponse<PaymentMethodConfigDto>? Refuse(
        UpdatePaymentMethodDto dto, string code, string? credentials)
    {
        if (dto.MinOrderAmount is { } floor && dto.MaxOrderAmount is { } ceiling && floor > ceiling)
        {
            return Invalid(
                PaymentErrors.AmountBandInvalid,
                "The smallest order is larger than the largest, so no order could use this.",
                nameof(dto.MaxOrderAmount));
        }

        if (dto.ChargeType == PaymentChargeType.Percent
            && dto.ChargeValue > PaymentMethodRules.MaxPercentCharge)
        {
            return Invalid(
                PaymentErrors.ChargeInvalid,
                "A charge of more than 100% is a typo rather than a policy.",
                nameof(dto.ChargeValue));
        }

        if (!dto.IsEnabled)
        {
            // Everything below is about a method customers will be offered. A
            // method being switched off can be in any state at all.
            return null;
        }

        if (!dto.AvailableInsideDhaka && !dto.AvailableOutsideDhaka)
        {
            return Invalid(
                PaymentErrors.NoZone,
                "This is switched on but offered nowhere. Choose at least one zone.",
                nameof(dto.AvailableInsideDhaka));
        }

        if (!_providers.TryGetValue(code, out var provider))
        {
            return GeneralResponse<PaymentMethodConfigDto>.Fail(
                PaymentErrors.NotImplemented,
                "Nothing in the shop can take money this way yet, so switching it on "
                + "would change nothing a customer sees.");
        }

        if (provider.Capabilities.NeedsCredentials && string.IsNullOrWhiteSpace(credentials))
        {
            return GeneralResponse<PaymentMethodConfigDto>.Fail(
                PaymentErrors.CredentialsRequired,
                "Add the merchant credentials before switching this on.");
        }

        return null;
    }

    /// <summary>
    /// What the stored credential becomes.
    /// </summary>
    /// <remarks>
    /// Three cases, and conflating any two of them loses a merchant key. Null
    /// — the field the screen never sends back, because it has nothing to send
    /// — keeps what is there. An empty string is a deliberate clearing.
    /// Anything else is a replacement, encrypted on the way in.
    /// </remarks>
    private string? Resolve(string? submitted, string? stored) =>
        submitted switch
        {
            null => stored,
            "" => null,
            _ => secrets.Protect(submitted.Trim())
        };

    private PaymentMethodConfigDto ToDto(PaymentMethodConfig config)
    {
        var implemented = _providers.TryGetValue(config.Code, out var provider);
        var capabilities = provider?.Capabilities ?? default;

        return new PaymentMethodConfigDto
        {
            Id = config.Id,
            Code = config.Code,
            DisplayNameEn = config.DisplayName.En,
            DisplayNameBn = config.DisplayName.Bn,
            DescriptionEn = config.Description?.En,
            DescriptionBn = config.Description?.Bn,
            IconUrl = config.IconUrl,
            IsEnabled = config.IsEnabled,
            SortOrder = config.SortOrder,
            Mode = config.Mode,
            MinOrderAmount = config.MinOrderAmount?.Amount,
            MaxOrderAmount = config.MaxOrderAmount?.Amount,
            AvailableInsideDhaka = config.AvailableInsideDhaka,
            AvailableOutsideDhaka = config.AvailableOutsideDhaka,
            ChargeType = config.ChargeType,
            ChargeValue = config.ChargeValue,
            HasCredentials = !string.IsNullOrWhiteSpace(config.Credentials),
            // Asked rather than assumed. A key ring rotated with a container
            // leaves a row that looks configured and cannot pay anybody, and
            // the shop should learn that here rather than from a customer.
            CredentialsUnreadable = !string.IsNullOrWhiteSpace(config.Credentials)
                && !secrets.TryUnprotect(config.Credentials, out _),
            IsImplemented = implemented,
            SupportsRedirect = capabilities.SupportsRedirect,
            SupportsRefund = capabilities.SupportsRefund,
            // A method with no provider yet is one being prepared, and what
            // there is to prepare is very largely the credential. Saying "it
            // needs none" because nothing has spoken up for it would hide the
            // one field that has to be filled in before the day it is built —
            // which is the whole reason the row exists ahead of the class.
            // A provider that has shipped answers for itself; cash says no.
            NeedsCredentials = !implemented || capabilities.NeedsCredentials
        };
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static GeneralResponse<PaymentMethodConfigDto> Invalid(
        string errorCode, string message, string field) =>
        GeneralResponse<PaymentMethodConfigDto>.Invalid(
            errorCode, message, new Dictionary<string, string[]> { [field] = [message] });
}

/// <summary>Event ids 2400–2401: configuring how the shop takes money.</summary>
public static partial class PaymentAdminLog
{
    [LoggerMessage(
        EventId = 2400,
        Level = LogLevel.Information,
        Message = "Payment method {Code} configured: enabled={IsEnabled}, mode={Mode}")]
    public static partial void MethodConfigured(
        ILogger logger, string code, bool isEnabled, string mode);
}
