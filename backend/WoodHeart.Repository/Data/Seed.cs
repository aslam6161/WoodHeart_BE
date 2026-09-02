using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Common;
using WoodHeart.Domain.Entity.Identity;
using WoodHeart.Domain.Enums.Common;

namespace WoodHeart.Repository.Data;

/// <summary>
/// Idempotent seeding, run at startup.
/// </summary>
/// <remarks>
/// Every step checks before it writes, so running it against a populated
/// database is a no-op. That is what makes it safe to leave in the startup path
/// rather than behind a flag someone has to remember to set.
/// </remarks>
public static class Seed
{
    public static async Task RunAsync(
        DataContext context,
        RoleManager<AppRole> roleManager,
        CancellationToken cancellationToken = default)
    {
        await SeedRolesAsync(roleManager);
        await SeedSettingsAsync(context, cancellationToken);
        await SeedFeatureFlagsAsync(context, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedRolesAsync(RoleManager<AppRole> roleManager)
    {
        foreach (var role in Roles.All)
        {
            if (await roleManager.RoleExistsAsync(role))
            {
                continue;
            }

            await roleManager.CreateAsync(new AppRole
            {
                Name = role,
                IsSystemRole = true,
                Description = role switch
                {
                    Roles.Admin => "Full access, including settings and payment configuration.",
                    Roles.Manager => "Catalog, inventory, orders and consultations. No settings.",
                    Roles.Staff => "Order fulfilment and consultation scheduling.",
                    _ => "A customer of the store."
                }
            });
        }
    }

    /// <summary>
    /// Seeds settings with placeholder values.
    /// </summary>
    /// <remarks>
    /// The tax and delivery numbers here are <b>placeholders, not the real
    /// figures</b> — those are open business questions (PLAN.md §16). They exist
    /// so the code paths that read them work; an admin sets the real values
    /// before go-live.
    /// </remarks>
    private static async Task SeedSettingsAsync(DataContext context, CancellationToken cancellationToken)
    {
        var defaults = new (string Key, string Value, SettingValueType Type, string Category, string Description)[]
        {
            (SettingKeys.VatRate, "0", SettingValueType.Decimal, "Tax",
                "VAT percentage applied at checkout. PLACEHOLDER — confirm the rate before go-live."),
            (SettingKeys.PricesIncludeVat, "true", SettingValueType.Boolean, "Tax",
                "Whether catalog prices already include VAT. Bangladeshi retail normally quotes inclusive."),
            (SettingKeys.VatOnDelivery, "false", SettingValueType.Boolean, "Tax",
                "Whether the delivery charge is itself taxable. PLACEHOLDER — confirm with the "
                + "shop's VAT registration."),
            (SettingKeys.DeliveryChargeInsideDhaka, "0", SettingValueType.Decimal, "Delivery",
                "Flat delivery charge inside Dhaka, in BDT. PLACEHOLDER."),
            (SettingKeys.DeliveryChargeOutsideDhaka, "0", SettingValueType.Decimal, "Delivery",
                "Flat delivery charge outside Dhaka, in BDT. PLACEHOLDER."),
            (SettingKeys.FreeDeliveryThreshold, "0", SettingValueType.Decimal, "Delivery",
                "Order total above which delivery is free. Zero disables the rule."),
            (SettingKeys.OrderNumberPrefix, "WH", SettingValueType.String, "Orders",
                "Prefix for human-facing order numbers, e.g. WH-2608-00042."),
            (SettingKeys.LowStockThreshold, "5", SettingValueType.Integer, "Inventory",
                "Units at or below which a product is flagged low on the admin dashboard.")
        };

        var existing = await context.StoreSettings
            .Select(x => x.Key)
            .ToListAsync(cancellationToken);

        foreach (var (key, value, type, category, description) in defaults)
        {
            if (existing.Contains(key))
            {
                continue;
            }

            context.StoreSettings.Add(new StoreSetting
            {
                Key = key,
                Value = value,
                ValueType = type,
                Category = category,
                Description = description,
                IsSystem = true
            });
        }
    }

    private static async Task SeedFeatureFlagsAsync(DataContext context, CancellationToken cancellationToken)
    {
        var defaults = new (string Name, bool Enabled, string Description)[]
        {
            // Off until the bKash merchant account is approved. Cash on delivery
            // is the only payment method until an admin flips this.
            (FeatureFlags.BkashEnabled, false, "Offer bKash at checkout."),
            (FeatureFlags.ConsultationsEnabled, true, "Show consultation booking on the storefront."),
            (FeatureFlags.ReviewsEnabled, false, "Allow customers to review products.")
        };

        var existing = await context.FeatureFlags
            .Select(x => x.Name)
            .ToListAsync(cancellationToken);

        foreach (var (name, enabled, description) in defaults)
        {
            if (existing.Contains(name))
            {
                continue;
            }

            context.FeatureFlags.Add(new FeatureFlag
            {
                Name = name,
                IsEnabled = enabled,
                Description = description
            });
        }
    }
}
