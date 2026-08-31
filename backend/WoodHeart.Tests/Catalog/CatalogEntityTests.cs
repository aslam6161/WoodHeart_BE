using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.Enums.Catalog;
using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Tests.Catalog;

/// <summary>
/// The catalog's derived properties. No database and no mocks — these are the
/// rules that decide what price a customer sees and which products are public,
/// so they are worth pinning down independently of any query.
/// </summary>
public class CatalogEntityTests
{
    // -------------------------------------------------------------------------
    // Category tree
    // -------------------------------------------------------------------------

    [Fact]
    public void Root_category_path_is_just_its_own_id()
    {
        var root = new Category { Id = 1, Name = LocalizedText.Create("Living Room"), Slug = Slug.From("living-room") };

        root.BuildPath(null).ShouldBe("/1/");
    }

    [Fact]
    public void Child_path_appends_to_the_parent_path()
    {
        var root = new Category { Id = 1, MaterializedPath = "/1/" };
        var child = new Category { Id = 14 };

        child.BuildPath(root).ShouldBe("/1/14/");
    }

    [Fact]
    public void Deep_path_composes_through_every_ancestor()
    {
        var root = new Category { Id = 1, MaterializedPath = "/1/" };
        var mid = new Category { Id = 14, MaterializedPath = "/1/14/" };
        var leaf = new Category { Id = 37 };

        mid.BuildPath(root).ShouldBe("/1/14/");
        leaf.BuildPath(mid).ShouldBe("/1/14/37/");
    }

    [Fact]
    public void Descendant_is_within_its_ancestor()
    {
        var root = new Category { Id = 1, MaterializedPath = "/1/" };
        var leaf = new Category { Id = 37, MaterializedPath = "/1/14/37/" };

        leaf.IsWithin(root).ShouldBeTrue();
        leaf.IsWithin(leaf).ShouldBeTrue();
        root.IsWithin(leaf).ShouldBeFalse();
    }

    [Fact]
    public void Sibling_ids_sharing_a_digit_prefix_are_not_descendants()
    {
        // The reason paths are wrapped in slashes. Compare "/1/14" against
        // "/1/140" as bare strings and category 140 looks like it lives under
        // 14 — every "everything under this category" query would then leak
        // products from an unrelated branch.
        var fourteen = new Category { Id = 14, MaterializedPath = "/1/14/" };
        var oneForty = new Category { Id = 140, MaterializedPath = "/1/140/" };

        oneForty.IsWithin(fourteen).ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // Variant pricing
    // -------------------------------------------------------------------------

    [Fact]
    public void Variant_without_an_override_inherits_the_product_price()
    {
        var product = NewProduct(basePrice: 45_000m);
        var variant = NewVariant(product, "WH-BED-SEGUN-6");

        variant.EffectivePrice.Amount.ShouldBe(45_000m);
    }

    [Fact]
    public void Variant_override_wins_over_the_product_price()
    {
        var product = NewProduct(basePrice: 45_000m);
        var variant = NewVariant(product, "WH-BED-SEGUN-7", priceOverride: 52_000m);

        variant.EffectivePrice.Amount.ShouldBe(52_000m);
    }

    [Fact]
    public void Variant_with_no_override_and_no_loaded_product_throws()
    {
        // A price that silently becomes zero because a query forgot an Include
        // is worse than a crash: it sells a 52,000 taka bed for nothing and
        // nobody notices until the accounts are reconciled.
        var orphan = new ProductVariant { Sku = "WH-ORPHAN-1", VariantName = "Orphan" };

        Should.Throw<InvalidOperationException>(() => orphan.EffectivePrice)
            .Message.ShouldContain("WH-ORPHAN-1");
    }

    [Fact]
    public void Offer_requires_a_compare_price_genuinely_above_the_selling_price()
    {
        var product = NewProduct(basePrice: 45_000m, compareAt: 60_000m);
        var onOffer = NewVariant(product, "WH-A");

        onOffer.IsOnOffer.ShouldBeTrue();

        // A "was" price at or below the current price is not a discount, and
        // rendering one as a strike-through is a false claim to a customer.
        var notAnOffer = NewVariant(NewProduct(basePrice: 45_000m, compareAt: 45_000m), "WH-B");
        notAnOffer.IsOnOffer.ShouldBeFalse();

        var noComparePrice = NewVariant(NewProduct(basePrice: 45_000m), "WH-C");
        noComparePrice.IsOnOffer.ShouldBeFalse();
    }

    [Fact]
    public void Variant_compare_price_override_wins_over_the_product_one()
    {
        var product = NewProduct(basePrice: 45_000m, compareAt: 50_000m);
        var variant = NewVariant(product, "WH-D", priceOverride: 52_000m);
        variant.CompareAtPriceOverride = Money.Taka(65_000m);

        variant.EffectiveCompareAtPrice!.Amount.ShouldBe(65_000m);
        variant.IsOnOffer.ShouldBeTrue();
    }

    // -------------------------------------------------------------------------
    // Default variant selection
    // -------------------------------------------------------------------------

    [Fact]
    public void Default_variant_is_the_flagged_one()
    {
        var product = NewProduct(basePrice: 1_000m);
        var first = NewVariant(product, "WH-1", sortOrder: 0);
        var flagged = NewVariant(product, "WH-2", sortOrder: 5);
        flagged.IsDefault = true;
        product.Variants = [first, flagged];

        product.DefaultVariant!.Sku.ShouldBe("WH-2");
    }

    [Fact]
    public void Default_variant_falls_back_to_the_lowest_sort_order()
    {
        var product = NewProduct(basePrice: 1_000m);
        product.Variants =
        [
            NewVariant(product, "WH-LATER", sortOrder: 9),
            NewVariant(product, "WH-FIRST", sortOrder: 1)
        ];

        product.DefaultVariant!.Sku.ShouldBe("WH-FIRST");
    }

    [Fact]
    public void Deleted_variants_are_never_chosen_as_the_default()
    {
        var product = NewProduct(basePrice: 1_000m);
        var deleted = NewVariant(product, "WH-GONE", sortOrder: 0);
        deleted.IsDefault = true;
        deleted.IsDeleted = true;

        product.Variants = [deleted, NewVariant(product, "WH-LIVE", sortOrder: 3)];

        product.DefaultVariant!.Sku.ShouldBe("WH-LIVE");
    }

    // -------------------------------------------------------------------------
    // Visibility
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(ProductStatus.Draft, false, false)]
    [InlineData(ProductStatus.Active, false, true)]
    [InlineData(ProductStatus.Archived, false, false)]
    [InlineData(ProductStatus.Active, true, false)]
    public void Only_active_undeleted_products_are_public(ProductStatus status, bool deleted, bool expected)
    {
        var product = NewProduct(basePrice: 100m);
        product.Status = status;
        product.IsDeleted = deleted;

        product.IsPubliclyVisible.ShouldBe(expected);
    }

    [Theory]
    [InlineData(ProductType.Stocked, true)]
    [InlineData(ProductType.MadeToOrder, false)]
    [InlineData(ProductType.Service, false)]
    public void Only_stocked_products_track_stock(ProductType type, bool expected)
    {
        // Made-to-order furniture has no on-hand quantity to draw down, and
        // reserving stock against it would block a sale that should succeed.
        var product = NewProduct(basePrice: 100m);
        product.ProductType = type;

        product.TracksStock.ShouldBe(expected);
    }

    // -------------------------------------------------------------------------
    // Collection scheduling
    // -------------------------------------------------------------------------

    [Fact]
    public void Collection_with_no_window_is_always_live()
    {
        var collection = NewCollection();

        collection.IsLiveAt(DateTimeOffset.UnixEpoch).ShouldBeTrue();
        collection.IsLiveAt(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)).ShouldBeTrue();
    }

    [Fact]
    public void Collection_is_not_live_before_it_starts_or_after_it_ends()
    {
        // The Eid collection case. Time is passed in rather than read from a
        // clock precisely so this can be asserted at all.
        var eid = NewCollection();
        eid.StartsAt = new DateTimeOffset(2026, 3, 20, 0, 0, 0, TimeSpan.Zero);
        eid.EndsAt = new DateTimeOffset(2026, 3, 31, 0, 0, 0, TimeSpan.Zero);

        eid.IsLiveAt(new DateTimeOffset(2026, 3, 19, 23, 59, 0, TimeSpan.Zero)).ShouldBeFalse();
        eid.IsLiveAt(new DateTimeOffset(2026, 3, 20, 0, 0, 0, TimeSpan.Zero)).ShouldBeTrue();
        eid.IsLiveAt(new DateTimeOffset(2026, 3, 25, 12, 0, 0, TimeSpan.Zero)).ShouldBeTrue();

        // EndsAt is exclusive: at exactly the end instant it is already over.
        eid.IsLiveAt(new DateTimeOffset(2026, 3, 31, 0, 0, 0, TimeSpan.Zero)).ShouldBeFalse();
    }

    [Fact]
    public void Inactive_or_deleted_collection_is_never_live()
    {
        var inactive = NewCollection();
        inactive.IsActive = false;
        inactive.IsLiveAt(DateTimeOffset.UtcNow).ShouldBeFalse();

        var deleted = NewCollection();
        deleted.IsDeleted = true;
        deleted.IsLiveAt(DateTimeOffset.UtcNow).ShouldBeFalse();
    }

    // -------------------------------------------------------------------------

    private static Product NewProduct(decimal basePrice, decimal? compareAt = null) => new()
    {
        Id = 1,
        Code = "WH-BED-001",
        Name = LocalizedText.Create("Segun King Bed", "সেগুন কিং বেড"),
        Slug = Slug.From("segun-king-bed"),
        CategoryId = 1,
        BasePrice = Money.Taka(basePrice),
        CompareAtPrice = compareAt is null ? null : Money.Taka(compareAt.Value),
        Status = ProductStatus.Active
    };

    private static ProductVariant NewVariant(
        Product product, string sku, decimal? priceOverride = null, int sortOrder = 0) => new()
        {
            ProductId = product.Id,
            Product = product,
            Sku = sku,
            VariantName = sku,
            SortOrder = sortOrder,
            PriceOverride = priceOverride is null ? null : Money.Taka(priceOverride.Value)
        };

    private static Collection NewCollection() => new()
    {
        Id = 1,
        Name = LocalizedText.Create("Shop the Bedroom"),
        Slug = Slug.From("shop-the-bedroom")
    };
}
