using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Enums.Catalog;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Repositories.Ordering;
using WoodHeart.Service.Mapping.Ordering;

namespace WoodHeart.Tests.Integration;

/// <summary>
/// The order queries, against PostgreSQL.
/// </summary>
/// <remarks>
/// One test, for one bug a substitute cannot see. The admin board prints
/// "3 items" on every row, and that figure is a sum over the order's lines —
/// which the search query did not load, so every order in the shop read
/// "0 items". A unit test with a substituted repository hands the mapper an
/// order whose lines are already attached in memory, and passes.
/// </remarks>
public class OrderRepositoryDatabaseTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [RequiresPostgresFact]
    public async Task The_board_query_loads_the_lines_the_item_count_is_summed_over()
    {
        await using var context = fixture.CreateContext();

        var variantId = await SeedVariantAsync(context);
        var order = await SeedOrderAsync(context, variantId, quantities: [1, 2]);

        // A fresh context, so nothing is still tracked from the seeding and
        // the lines can only come from the query itself.
        await using var reading = fixture.CreateContext();
        var repository = new OrderRepository(reading);

        var rows = await repository.SearchAsync(status: null, term: null, skip: 0, take: 20);

        var found = rows.ShouldHaveSingleItem();
        found.OrderNumber.ShouldBe(order.OrderNumber);
        AdminOrderMapper.ToSummary(found).ItemCount.ShouldBe(3);
    }

    private static async Task<long> SeedVariantAsync(DataContext context)
    {
        var category = new Category
        {
            Name = LocalizedText.Create("Beds"),
            Slug = Slug.From($"beds-{Guid.NewGuid():n}"),
            MaterializedPath = "/"
        };

        await context.Categories.AddAsync(category);
        await context.SaveChangesAsync();

        category.MaterializedPath = $"/{category.Id}/";

        var product = new Product
        {
            Code = $"WH-{Guid.NewGuid():n}"[..12],
            Name = LocalizedText.Create("Segun King Bed"),
            Slug = Slug.From($"segun-king-bed-{Guid.NewGuid():n}"),
            CategoryId = category.Id,
            BasePrice = Money.Taka(68_500m),
            Status = ProductStatus.Active
        };

        await context.Products.AddAsync(product);
        await context.SaveChangesAsync();

        var variant = new ProductVariant
        {
            ProductId = product.Id,
            Sku = $"SKU-{Guid.NewGuid():n}"[..16],
            VariantName = "Segun · 6ft",
            IsDefault = true
        };

        await context.ProductVariants.AddAsync(variant);
        await context.SaveChangesAsync();

        return variant.Id;
    }

    private static async Task<Order> SeedOrderAsync(DataContext context, long variantId, int[] quantities)
    {
        var order = new Order
        {
            OrderNumber = $"WH-TEST-{Guid.NewGuid():n}"[..16],
            ContactName = "Rakib Hasan",
            ContactPhone = "+8801712345678",
            ShippingAddress = DeliveryAddress.Create("Dhaka", "Dhaka", "House 12, Road 3", area: "Dhanmondi"),
            DeliveryZone = DeliveryZone.InsideDhaka,
            Currency = Money.Bdt,
            Subtotal = Money.Taka(205_500m),
            DiscountTotal = Money.Zero(),
            GoodsNet = Money.Taka(191_163m),
            VatAmount = Money.Taka(14_337m),
            VatRatePercent = 7.5m,
            PricesIncludeVat = true,
            DeliveryFee = Money.Zero(),
            PaymentSurcharge = Money.Zero(),
            GrandTotal = Money.Taka(205_500m),
            PaymentMethodCode = PaymentMethodCodes.CashOnDelivery,
            PlacedAt = DateTimeOffset.UtcNow,
            Lines =
            [
                .. quantities.Select(quantity => new OrderLine
                {
                    ProductVariantId = variantId,
                    ProductId = 0,
                    ProductNameEn = "Segun King Bed",
                    ProductSlug = "segun-king-bed",
                    Sku = "WH-BED-001",
                    VariantName = "Segun · 6ft",
                    Quantity = quantity,
                    UnitPrice = Money.Taka(68_500m),
                    DiscountAmount = Money.Zero(),
                    LineTotal = Money.Taka(68_500m * quantity),
                    DeliveryChargeApplied = Money.Zero()
                })
            ]
        };

        // The line's ProductId is a plain column, not a foreign key that EF
        // would fill in; it points at the product the variant belongs to.
        var productId = (await context.ProductVariants.FindAsync(variantId))!.ProductId;

        foreach (var line in order.Lines)
        {
            line.ProductId = productId;
        }

        await context.Orders.AddAsync(order);
        await context.SaveChangesAsync();

        return order;
    }
}
