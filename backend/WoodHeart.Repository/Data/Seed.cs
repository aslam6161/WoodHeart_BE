using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Common;
using WoodHeart.Domain.Entity.Identity;
using WoodHeart.Domain.Entity.Payments;
using WoodHeart.Domain.Enums.Common;
using WoodHeart.Domain.Enums.Payments;
using WoodHeart.Domain.ValueObjects;

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
        await SeedPaymentMethodsAsync(context, cancellationToken);

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
            (SettingKeys.VatRate, "7.5", SettingValueType.Decimal, "Tax",
                "VAT percentage applied at checkout. Admin-editable — change it here, not in code."),
            (SettingKeys.PricesIncludeVat, "true", SettingValueType.Boolean, "Tax",
                "Whether catalog prices already include VAT. Bangladeshi retail normally quotes inclusive."),
            (SettingKeys.VatOnDelivery, "false", SettingValueType.Boolean, "Tax",
                "Whether the delivery charge is itself taxable. PLACEHOLDER — confirm with the "
                + "shop's VAT registration."),
            (SettingKeys.DeliveryChargeInsideDhaka, "0", SettingValueType.Decimal, "Delivery",
                "Fallback inside-Dhaka delivery charge for products that have not been costed "
                + "individually. Delivery is priced per product; this is the default, not the price."),
            (SettingKeys.DeliveryChargeOutsideDhaka, "0", SettingValueType.Decimal, "Delivery",
                "The same fallback, for everywhere outside Dhaka."),
            (SettingKeys.FreeDeliveryThreshold, "0", SettingValueType.Decimal, "Delivery",
                "Order total above which delivery is free. Zero disables the rule. Note that with "
                + "per-product delivery this waives the whole charge, bulky items included."),
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

    /// <summary>
    /// The payment methods the shop starts with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cash on delivery is enabled, because it is how most furniture in this
    /// market is paid for. bKash is seeded <b>disabled</b> and stays that way
    /// until the merchant account is approved — the row exists now so that day
    /// is a toggle in an admin screen rather than a release.
    /// </para>
    /// <para>
    /// No charge on either, and no cash-on-delivery ceiling. Both are real
    /// business decisions (PLAN.md §16) rather than technical ones, so the
    /// seed leaves them at "no restriction" and the shop sets them when it has
    /// decided. The alternative — inventing a 100,000৳ ceiling here — would
    /// silently refuse a real order somebody wanted to place.
    /// </para>
    /// </remarks>
    private static async Task SeedPaymentMethodsAsync(
        DataContext context, CancellationToken cancellationToken)
    {
        var defaults = new[]
        {
            new PaymentMethodConfig
            {
                Code = PaymentMethodCodes.CashOnDelivery,
                DisplayName = LocalizedText.Create("Cash on delivery", "ক্যাশ অন ডেলিভারি"),
                Description = LocalizedText.Create(
                    "Pay the rider in cash when your order arrives.",
                    "আপনার অর্ডার পৌঁছালে ডেলিভারি ম্যানকে নগদ পরিশোধ করুন।"),
                IsEnabled = true,
                SortOrder = 10,
                Mode = PaymentMode.Live,
                ChargeType = PaymentChargeType.None
            },
            new PaymentMethodConfig
            {
                Code = PaymentMethodCodes.Bkash,
                DisplayName = LocalizedText.Create("bKash", "বিকাশ"),
                Description = LocalizedText.Create(
                    "Pay securely from your bKash account.",
                    "আপনার বিকাশ অ্যাকাউন্ট থেকে নিরাপদে পরিশোধ করুন।"),
                IsEnabled = false,
                SortOrder = 20,
                Mode = PaymentMode.Sandbox,
                ChargeType = PaymentChargeType.None
            }
        };

        var existing = await context.PaymentMethodConfigs
            .Select(x => x.Code)
            .ToListAsync(cancellationToken);

        foreach (var method in defaults.Where(m => !existing.Contains(m.Code)))
        {
            context.PaymentMethodConfigs.Add(method);
        }
    }
}
