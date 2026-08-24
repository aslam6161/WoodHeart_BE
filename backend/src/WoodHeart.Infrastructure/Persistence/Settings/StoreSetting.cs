namespace WoodHeart.Infrastructure.Persistence.Settings;

/// <summary>
/// A runtime-configurable setting the admin can change without a deployment.
/// </summary>
/// <remarks>
/// <para>
/// Things that live here: VAT rate, whether displayed prices include VAT, free
/// delivery threshold, store contact details, order-number prefix, low-stock
/// threshold, abandoned-cart delay.
/// </para>
/// <para>
/// Things that do NOT live here: connection strings, signing keys, gateway
/// credentials. Those are secrets and belong in environment variables or a
/// secret store — a setting is something a shop manager may safely change at
/// 2pm on a Tuesday.
/// </para>
/// </remarks>
public sealed class StoreSetting
{
    /// <summary>Dotted key, e.g. <c>tax.vat_rate</c> or <c>delivery.free_threshold</c>.</summary>
    public required string Key { get; set; }

    public required string Value { get; set; }

    public SettingValueType ValueType { get; set; } = SettingValueType.String;

    /// <summary>Groups keys in the admin UI: Tax, Delivery, Store, Orders, Notifications.</summary>
    public required string Category { get; set; }

    public string? Description { get; set; }

    /// <summary>System settings are editable but never deletable.</summary>
    public bool IsSystem { get; set; }

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }
}

public enum SettingValueType
{
    String = 0,
    Integer = 1,
    Decimal = 2,
    Boolean = 3,
    Json = 4
}

/// <summary>
/// A runtime feature flag, e.g. <c>bkash.enabled</c>, <c>reviews.enabled</c>.
/// </summary>
/// <remarks>
/// Separate from <see cref="StoreSetting"/> on purpose: flags gate whole code
/// paths and get read on hot paths, so they are cached aggressively and have a
/// dedicated, tiny table.
/// </remarks>
public sealed class FeatureFlag
{
    public required string Name { get; set; }

    public bool IsEnabled { get; set; }

    public string? Description { get; set; }

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }
}
