using WoodHeart.Domain.Enums.Common;

namespace WoodHeart.Domain.Entity.Common;

/// <summary>
/// A runtime-configurable setting the admin can change without a deployment.
/// </summary>
/// <remarks>
/// <para>
/// Things that live here: VAT rate, whether displayed prices include VAT, the
/// free-delivery threshold, delivery charges inside and outside Dhaka, store
/// contact details, order-number prefix, low-stock threshold.
/// </para>
/// <para>
/// Things that do NOT live here: connection strings, signing keys, bKash
/// credentials. Those are secrets and belong in environment variables or a
/// secret store. The test is simple — a setting is something a shop manager may
/// safely change at 2pm on a Tuesday.
/// </para>
/// </remarks>
public class StoreSetting : BaseEntity
{
    /// <summary>Dotted key, e.g. <c>tax.vat_rate</c> or <c>delivery.free_threshold</c>.</summary>
    public string Key { get; set; } = null!;

    public string Value { get; set; } = null!;

    public SettingValueType ValueType { get; set; } = SettingValueType.String;

    /// <summary>Groups keys in the admin UI: Tax, Delivery, Store, Orders, Notifications.</summary>
    public string Category { get; set; } = null!;

    public string? Description { get; set; }

    /// <summary>System settings are editable but never deletable.</summary>
    public bool IsSystem { get; set; }
}

/// <summary>
/// A runtime feature flag, e.g. <c>bkash.enabled</c>, <c>reviews.enabled</c>.
/// </summary>
/// <remarks>
/// Separate from <see cref="StoreSetting"/> on purpose: flags gate whole code
/// paths and get read on hot paths, so they are cached aggressively and given a
/// dedicated, tiny table. <c>bkash.enabled</c> in particular is the switch that
/// lets payment methods go live without a redeploy.
/// </remarks>
public class FeatureFlag : BaseEntity
{
    public string Name { get; set; } = null!;

    public bool IsEnabled { get; set; }

    public string? Description { get; set; }
}
