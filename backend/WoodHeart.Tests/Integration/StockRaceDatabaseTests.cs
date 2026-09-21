using Microsoft.EntityFrameworkCore;
using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.Entity.Inventory;
using WoodHeart.Domain.Enums.Catalog;
using WoodHeart.Domain.Enums.Inventory;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Repositories.Inventory;

namespace WoodHeart.Tests.Integration;

/// <summary>
/// Two checkouts, one bed, against PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// The unit tests prove the arithmetic; this proves the guard. Two contexts
/// each load the same stock item showing one available, each reserve it,
/// and each try to save. Without the row version both saves succeed and
/// the shop has sold a bed it does not have. With it, the second save
/// fails with a concurrency exception, which the pipeline turns into a 409
/// and the customer's retry sees zero.
/// </para>
/// <para>
/// This is the case PLAN §12 calls non-negotiable: one of three places
/// money is lost silently.
/// </para>
/// </remarks>
public class StockRaceDatabaseTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);

    [RequiresPostgresFact]
    public async Task Two_checkouts_cannot_both_take_the_last_unit()
    {
        long variantId;

        await using (var seeding = fixture.CreateContext())
        {
            variantId = await SeedStockedVariantAsync(seeding, onHand: 1);
        }

        // Two customers, two connections, one row each, both showing one.
        await using var first = fixture.CreateContext();
        await using var second = fixture.CreateContext();

        var firstItem = await new StockRepository(first).GetByVariantAsync(variantId);
        var secondItem = await new StockRepository(second).GetByVariantAsync(variantId);

        firstItem!.Available.ShouldBe(1);
        secondItem!.Available.ShouldBe(1);

        firstItem.Reserve(1);
        secondItem.Reserve(1);

        await first.SaveChangesAsync();

        // The second save carries the xmin it read, which the first save
        // has moved on. PostgreSQL says so; EF turns it into this.
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        await using var reading = fixture.CreateContext();
        var stored = await new StockRepository(reading).GetByVariantAsync(variantId);

        stored!.Reserved.ShouldBe(1);
        stored.Available.ShouldBe(0);
    }

    [RequiresPostgresFact]
    public async Task The_ledger_is_kept_on_the_way_through()
    {
        long variantId;

        await using (var seeding = fixture.CreateContext())
        {
            variantId = await SeedStockedVariantAsync(seeding, onHand: 4);
        }

        await using (var context = fixture.CreateContext())
        {
            var repository = new StockRepository(context);
            var item = (await repository.GetByVariantAsync(variantId))!;

            await repository.AddMovementAsync(item.Apply(StockMovementType.Damage, 1, "Rakib", Now, reason: "cracked"));
            item.Reserve(2);
            await repository.AddMovementAsync(item.Commit(2, orderId: 0, "Rakib", Now.AddHours(1)));

            await context.SaveChangesAsync();
        }

        await using var reading = fixture.CreateContext();
        var rows = await new StockRepository(reading).GetMovementsAsync(variantId, 0, 10);

        // Newest first, each with the running count, and the opening line
        // at the bottom.
        rows.Select(r => (r.Type, r.Quantity, r.OnHandAfter)).ShouldBe(
        [
            (StockMovementType.Sale, -2, 1),
            (StockMovementType.Damage, -1, 3),
            (StockMovementType.Purchase, 4, 4)
        ]);
    }

    private static async Task<long> SeedStockedVariantAsync(DataContext context, int onHand)
    {
        var category = new Category
        {
            Name = LocalizedText.Create("Beds"),
            Slug = Slug.From($"beds-{Guid.NewGuid():n}"),
            MaterializedPath = "/"
        };

        context.Categories.Add(category);
        await context.SaveChangesAsync();
        category.MaterializedPath = $"/{category.Id}/";

        var product = new Product
        {
            Code = $"WH-{Guid.NewGuid():n}"[..12],
            Name = LocalizedText.Create("Segun King Bed"),
            Slug = Slug.From($"segun-king-bed-{Guid.NewGuid():n}"),
            CategoryId = category.Id,
            BasePrice = Money.Taka(68_500m),
            ProductType = ProductType.Stocked,
            Status = ProductStatus.Active
        };

        context.Products.Add(product);
        await context.SaveChangesAsync();

        var variant = new ProductVariant
        {
            ProductId = product.Id,
            Sku = $"SKU-{Guid.NewGuid():n}"[..16],
            VariantName = "Segun · 6ft",
            IsDefault = true
        };

        context.ProductVariants.Add(variant);
        await context.SaveChangesAsync();

        var item = new StockItem { ProductVariantId = variant.Id };
        var opening = item.Apply(StockMovementType.Purchase, onHand, "Seed", Now.AddDays(-1), reason: "opening");

        context.StockItems.Add(item);
        context.StockMovements.Add(opening);
        await context.SaveChangesAsync();

        return variant.Id;
    }
}
