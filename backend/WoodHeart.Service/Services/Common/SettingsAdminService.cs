using System.Globalization;
using Microsoft.Extensions.Logging;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Common;
using WoodHeart.Domain.Enums.Common;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Common;
using WoodHeart.Service.DTOs.Common;
using WoodHeart.Service.Interfaces.Common;

namespace WoodHeart.Service.Services.Common;

/// <summary>
/// Reads and writes the store settings table for the admin screen.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every value is checked against a rule before any is written.</b> The
/// generic rule is the setting's declared type: a decimal parses as one, a
/// boolean is <c>true</c> or <c>false</c>. On top of that, the settings that
/// carry money or a legal meaning have their own bounds — a VAT rate of 750
/// is a slipped decimal point, not a policy, and it would be applied to the
/// next order placed. What is stored is the normalised spelling, and
/// <c>7,5</c> typed on a machine with a European locale is refused rather
/// than stored as a string the reader later parses as 75.
/// </para>
/// <para>
/// The cache in front of the read service is invalidated key by key after the
/// commit, not before: a reader that raced the write would otherwise refill
/// the cache with the old value for another five minutes.
/// </para>
/// </remarks>
public class SettingsAdminService(
    IStoreSettingRepository settings,
    IUnitOfWork unitOfWork,
    IStoreSettingService cache,
    ICurrentUserService currentUser,
    ILogger<SettingsAdminService> logger) : ISettingsAdminService
{
    /// <summary>The screen's section order. Anything else goes last, alphabetically.</summary>
    private static readonly string[] CategoryOrder = ["Store", "Tax", "Delivery", "Orders", "Inventory"];

    /// <summary>
    /// The order within a section: the thing you fill in first, first. A key
    /// not listed here goes after the listed ones, alphabetically, so a new
    /// setting is shown before anybody remembers to place it.
    /// </summary>
    private static readonly string[] KeyOrder =
    [
        SettingKeys.StoreName, SettingKeys.StoreAddress, SettingKeys.StorePhone,
        SettingKeys.StoreEmail, SettingKeys.StoreBin,
        SettingKeys.VatRate, SettingKeys.PricesIncludeVat, SettingKeys.VatOnDelivery,
        SettingKeys.DeliveryChargeInsideDhaka, SettingKeys.DeliveryChargeOutsideDhaka,
        SettingKeys.FreeDeliveryThreshold,
        SettingKeys.OrderNumberPrefix,
        SettingKeys.LowStockThreshold
    ];

    public async Task<GeneralResponse<IReadOnlyList<StoreSettingDto>>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var all = await settings.GetAllAsync(cancellationToken);

        return GeneralResponse<IReadOnlyList<StoreSettingDto>>.Success(ToDtos(all));
    }

    public async Task<GeneralResponse<IReadOnlyList<StoreSettingDto>>> UpdateAsync(
        UpdateSettingsDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var all = await settings.GetAllAsync(cancellationToken);
        var byKey = all.ToDictionary(x => x.Key, StringComparer.Ordinal);

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var changes = new List<(StoreSetting Setting, string From, string To)>();

        foreach (var (key, raw) in dto.Values)
        {
            if (!byKey.TryGetValue(key, out var setting))
            {
                // Refused rather than created. The screen edits the settings
                // the shop has; a key nothing reads would be a field that
                // looks like it does something.
                return GeneralResponse<IReadOnlyList<StoreSettingDto>>.Fail(
                    SettingsErrors.UnknownKey, $"There is no setting called '{key}'.");
            }

            var outcome = Normalise(setting, raw);

            if (outcome.Error is { } error)
            {
                errors[key] = [error];
                continue;
            }

            if (!string.Equals(setting.Value, outcome.Value, StringComparison.Ordinal))
            {
                changes.Add((setting, setting.Value, outcome.Value!));
            }
        }

        if (errors.Count > 0)
        {
            return GeneralResponse<IReadOnlyList<StoreSettingDto>>.Invalid(
                SettingsErrors.InvalidValue, "Please correct the highlighted settings.", errors);
        }

        if (changes.Count == 0)
        {
            return GeneralResponse<IReadOnlyList<StoreSettingDto>>.Success(ToDtos(all), "Nothing changed.");
        }

        foreach (var (setting, _, to) in changes)
        {
            setting.Value = to;
            settings.Update(setting);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // After the commit, so a reader cannot refill the cache with the old
        // value between the invalidation and the write landing.
        foreach (var (setting, from, to) in changes)
        {
            cache.Invalidate(setting.Key);
            SettingsLog.SettingChanged(logger, setting.Key, from, to, currentUser.PhoneNumber ?? "system");
        }

        return GeneralResponse<IReadOnlyList<StoreSettingDto>>.Success(
            ToDtos(all), $"Saved {changes.Count} {(changes.Count == 1 ? "setting" : "settings")}.");
    }

    /// <summary>
    /// The value as it will be stored, or why it cannot be.
    /// </summary>
    /// <remarks>
    /// Public and static so the rules can be tested without a database, and
    /// so the list of per-key rules is in one place. When a setting gains a
    /// rule, it gains it here.
    /// </remarks>
    public static (string? Value, string? Error) Normalise(StoreSetting setting, string? raw)
    {
        ArgumentNullException.ThrowIfNull(setting);

        var text = (raw ?? string.Empty).Trim();

        switch (setting.ValueType)
        {
            case SettingValueType.Decimal:
            {
                // No thousands separator allowed. With it, "7,5" typed on a
                // European-locale machine parses as 75 — and the reader would
                // then charge 75% VAT. Refused here; refilled by the admin.
                const NumberStyles plain = NumberStyles.AllowLeadingSign
                                           | NumberStyles.AllowDecimalPoint
                                           | NumberStyles.AllowLeadingWhite
                                           | NumberStyles.AllowTrailingWhite;

                if (!decimal.TryParse(text, plain, CultureInfo.InvariantCulture, out var value))
                {
                    return (null, "Enter a number, with a dot for decimals.");
                }

                if (value < 0)
                {
                    return (null, "Cannot be negative.");
                }

                if (setting.Key == SettingKeys.VatRate && value > 100)
                {
                    return (null, "A VAT rate is a percentage — 100 at most.");
                }

                return (value.ToString(CultureInfo.InvariantCulture), null);
            }

            case SettingValueType.Integer:
            {
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                {
                    return (null, "Enter a whole number.");
                }

                if (value < 0)
                {
                    return (null, "Cannot be negative.");
                }

                return (value.ToString(CultureInfo.InvariantCulture), null);
            }

            case SettingValueType.Boolean:
            {
                if (!bool.TryParse(text, out var value))
                {
                    return (null, "Must be on or off.");
                }

                return (value ? "true" : "false", null);
            }

            default:
                return NormaliseString(setting.Key, text);
        }
    }

    private static (string? Value, string? Error) NormaliseString(string key, string text)
    {
        switch (key)
        {
            case SettingKeys.OrderNumberPrefix:
                // In every order number the shop will ever issue, and quoted
                // over the phone. Short, letters only, and upper case so that
                // "wh-2609-00042" and "WH-2609-00042" are the same order.
                if (text.Length is < 1 or > 6 || !text.All(char.IsAsciiLetter))
                {
                    return (null, "One to six letters.");
                }

                return (text.ToUpperInvariant(), null);

            case SettingKeys.StoreName:
                if (text.Length == 0)
                {
                    return (null, "The shop needs a name on its invoices.");
                }

                return (text.Length <= 120 ? text : null, text.Length <= 120 ? null : "120 characters at most.");

            case SettingKeys.StorePhone:
                if (text.Length == 0)
                {
                    return (string.Empty, null);
                }

                // Normalised to E.164 like every other number in the system,
                // so the invoice, the SMS sender id and the contact page
                // cannot each spell it differently.
                return PhoneNumber.TryParse(text, out var phone) && phone is not null
                    ? (phone.Value, null)
                    : (null, "Enter a Bangladeshi mobile or landline number.");

            case SettingKeys.StoreEmail:
                if (text.Length == 0)
                {
                    return (string.Empty, null);
                }

                return EmailAddress.TryParse(text, out var email) && email is not null
                    ? (email.Value, null)
                    : (null, "That does not look like an email address.");

            case SettingKeys.StoreBin:
                // NBR BINs are digits, historically with hyphens. Digits are
                // kept as typed rather than validated to a length the NBR may
                // change; what is refused is text that is plainly not one.
                if (text.Length > 0 && !text.All(c => char.IsAsciiDigit(c) || c == '-'))
                {
                    return (null, "A BIN is digits, with hyphens if you like.");
                }

                return (text, null);

            default:
                return text.Length <= 500 ? (text, null) : (null, "500 characters at most.");
        }
    }

    private static IReadOnlyList<StoreSettingDto> ToDtos(IReadOnlyList<StoreSetting> all) =>
        [.. all
            .OrderBy(x => Rank(CategoryOrder, x.Category))
            .ThenBy(x => x.Category, StringComparer.Ordinal)
            .ThenBy(x => Rank(KeyOrder, x.Key))
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new StoreSettingDto
            {
                Key = x.Key,
                Value = x.Value,
                ValueType = x.ValueType,
                Category = x.Category,
                Description = x.Description,
                UpdatedAt = x.UpdatedAt
            })];

    /// <summary>Position in <paramref name="order"/>, or one past the end when absent.</summary>
    private static int Rank(string[] order, string value)
    {
        var index = Array.IndexOf(order, value);

        return index >= 0 ? index : order.Length;
    }
}

internal static partial class SettingsLog
{
    /// <summary>
    /// Every change, with both values and who made it. A VAT rate that
    /// changed on a Tuesday afternoon is a question somebody will ask.
    /// </summary>
    [LoggerMessage(
        EventId = 1800,
        Level = LogLevel.Information,
        Message = "Setting {Key} changed '{From}' -> '{To}' by {Actor}.")]
    public static partial void SettingChanged(
        ILogger logger, string key, string from, string to, string actor);
}
