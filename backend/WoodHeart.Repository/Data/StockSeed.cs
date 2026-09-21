using Microsoft.EntityFrameworkCore;
using WoodHeart.Domain.Entity.Inventory;
using WoodHeart.Domain.Enums.Catalog;
using WoodHeart.Domain.Enums.Inventory;
using WoodHeart.Domain.Helpers;

namespace WoodHeart.Repository.Data;

/// <summary>
/// Opening stock for the sample catalogue.
/// </summary>
/// <remarks>
/// <para>
/// Gated with <see cref="CatalogSeed"/> and for the same reason: a dozen
/// sample sofas need a count or nothing in the sample shop can be bought,
/// and a real shop's count comes from a real stock-in, not from here.
/// </para>
/// <para>
/// Idempotent by variant: a variant that already has a count is left alone,
/// so this can run on every boot and never doubles anything. The opening
/// quantity goes through the ledger like any other movement — even sample
/// stock has a line saying where it came from.
/// </para>
/// </remarks>
public static class StockSeed
{
    private const int OpeningQuantity = 10;

    public static async Task RunAsync(
        DataContext context, IDateTimeProvider clock, CancellationToken cancellationToken = default)
    {
        var stocked = await context.ProductVariants
            .IgnoreQueryFilters()
            .Where(v => v.IsActive && !v.IsDeleted && v.Product.ProductType == ProductType.Stocked)
            .Where(v => v.Stock == null)
            .Select(v => v.Id)
            .ToListAsync(cancellationToken);

        if (stocked.Count == 0)
        {
            return;
        }

        var now = clock.UtcNow;

        foreach (var variantId in stocked)
        {
            var item = new StockItem { ProductVariantId = variantId };

            var movement = item.Apply(
                StockMovementType.Purchase, OpeningQuantity, "Seed", now,
                reason: "Opening stock for the sample catalogue.");

            context.StockItems.Add(item);
            context.StockMovements.Add(movement);
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
