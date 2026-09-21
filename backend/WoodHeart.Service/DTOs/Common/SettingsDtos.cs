using System.ComponentModel.DataAnnotations;
using WoodHeart.Domain.Enums.Common;

namespace WoodHeart.Service.DTOs.Common;

/// <summary>One runtime setting, as the admin screen shows it.</summary>
public class StoreSettingDto
{
    public string Key { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;

    public SettingValueType ValueType { get; init; }

    /// <summary>Tax, Delivery, Store, Orders, Inventory — the screen's sections.</summary>
    public string Category { get; init; } = string.Empty;

    public string? Description { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>
/// The whole screen, saved at once.
/// </summary>
/// <remarks>
/// <para>
/// One request rather than one per field, and all-or-nothing: the VAT rate
/// and "prices include VAT" are one decision, and a save that landed the
/// first and refused the second would leave the shop charging tax it does
/// not show. Every value is validated before any is written.
/// </para>
/// <para>
/// Keys not in the dictionary are left alone, so the client sends what it
/// edited. Values arrive as strings whatever their type — the service parses
/// them against the setting's declared type with the invariant culture, and
/// stores the normalised spelling.
/// </para>
/// </remarks>
public class UpdateSettingsDto
{
    [Required]
    [MinLength(1, ErrorMessage = "Nothing to save.")]
    public Dictionary<string, string?> Values { get; init; } = [];
}
