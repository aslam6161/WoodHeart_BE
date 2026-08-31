using NSubstitute;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.Enums.Catalog;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Catalog;
using WoodHeart.Repository.Queries;
using WoodHeart.Service.Services.Catalog;
using WoodHeart.Tests.Helper;

namespace WoodHeart.Tests.Catalog;

/// <summary>
/// The public catalog, and mostly one question: can anything unpublished get
/// out?
/// </summary>
public class StorefrontServiceTests
{
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();
    private readonly ICollectionRepository _collections = Substitute.For<ICollectionRepository>();
    private readonly FakeClock _clock = new();

    public StorefrontServiceTests()
    {
        _categories.GetProductCountsAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<long, int>());
        _categories.GetAncestorsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _products.SearchAsync(Arg.Any<ProductQuery>(), Arg.Any<CancellationToken>())
            .Returns(PagedList<Product>.Empty(1, 20));
    }

    private StorefrontService CreateService() =>
        new(_products, _categories, _collections, _clock);

    // -------------------------------------------------------------------------
    // Nothing unpublished escapes
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(ProductStatus.Draft)]
    [InlineData(ProductStatus.Archived)]
    public async Task An_unpublished_product_is_not_reachable_by_slug(ProductStatus status)
    {
        // Guessing a slug must not expose half-written copy and a price nobody
        // has approved.
        var product = NewProduct();
        product.Status = status;
        _products.GetBySlugWithDetailsAsync("segun-king-bed", Arg.Any<CancellationToken>())
            .Returns(product);

        var result = await CreateService().GetProductAsync("segun-king-bed");

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(CatalogErrors.ProductNotFound);
    }

    [Fact]
    public async Task A_soft_deleted_product_is_not_reachable_by_slug()
    {
        var product = NewProduct();
        product.IsDeleted = true;
        _products.GetBySlugWithDetailsAsync("segun-king-bed", Arg.Any<CancellationToken>())
            .Returns(product);

        var result = await CreateService().GetProductAsync("segun-king-bed");

        result.IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public async Task A_missing_and_an_unpublished_product_are_indistinguishable()
    {
        // Deliberate. Different responses would confirm to anyone enumerating
        // slugs that a product exists but is not live yet — which is exactly
        // what a competitor wants to know before a launch.
        var draft = NewProduct();
        draft.Status = ProductStatus.Draft;
        _products.GetBySlugWithDetailsAsync("draft-item", Arg.Any<CancellationToken>()).Returns(draft);
        _products.GetBySlugWithDetailsAsync("no-such-item", Arg.Any<CancellationToken>())
            .Returns((Product?)null);

        var service = CreateService();
        var forDraft = await service.GetProductAsync("draft-item");
        var forMissing = await service.GetProductAsync("no-such-item");

        forDraft.ErrorCode.ShouldBe(forMissing.ErrorCode);
        forDraft.IsSuccess.ShouldBeFalse();
        forMissing.IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public async Task A_caller_supplied_status_filter_is_overwritten_not_honoured()
    {
        // The whole storefront's safety rests on this. ?status=Draft binds
        // happily onto the query model; forcing Active afterwards is the only
        // thing between that and an unpublished price list.
        ProductQuery? captured = null;
        _products.SearchAsync(
                Arg.Do<ProductQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(PagedList<Product>.Empty(1, 20));

        await CreateService().SearchAsync(new ProductQuery { Status = ProductStatus.Draft });

        captured.ShouldNotBeNull();
        captured!.Status.ShouldBe(ProductStatus.Active);
    }

    [Fact]
    public async Task The_public_query_keeps_every_other_filter_the_caller_set()
    {
        // Overwriting status must not quietly drop the customer's actual filters.
        ProductQuery? captured = null;
        _products.SearchAsync(
                Arg.Do<ProductQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(PagedList<Product>.Empty(2, 40));

        await CreateService().SearchAsync(new ProductQuery
        {
            PageNumber = 2,
            PageSize = 40,
            CategoryId = 7,
            IncludeDescendantCategories = true,
            BrandId = 3,
            Search = "সেগুন",
            MinPrice = 1000m,
            MaxPrice = 90000m,
            SortBy = ProductSort.PriceLowToHigh
        });

        captured!.PageNumber.ShouldBe(2);
        captured.PageSize.ShouldBe(40);
        captured.CategoryId.ShouldBe(7);
        captured.IncludeDescendantCategories.ShouldBeTrue();
        captured.BrandId.ShouldBe(3);
        captured.Search.ShouldBe("সেগুন");
        captured.MinPrice.ShouldBe(1000m);
        captured.MaxPrice.ShouldBe(90000m);
        captured.SortBy.ShouldBe(ProductSort.PriceLowToHigh);
    }

    [Fact]
    public async Task The_public_tree_never_asks_for_inactive_categories()
    {
        await CreateService().GetCategoryTreeAsync();

        await _categories.Received(1).GetTreeAsync(false, Arg.Any<CancellationToken>());
        await _categories.DidNotReceive().GetTreeAsync(true, Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // Collection scheduling
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_collection_outside_its_window_is_not_reachable()
    {
        var eid = NewCollection();
        eid.StartsAt = FakeClock.DefaultNow.AddDays(7);
        _collections.GetBySlugAsync("eid", Arg.Any<CancellationToken>()).Returns(eid);

        var result = await CreateService().GetCollectionAsync("eid");

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(CatalogErrors.CollectionNotFound);
    }

    [Fact]
    public async Task A_live_collection_is_returned_with_its_seo()
    {
        var collection = NewCollection();
        _collections.GetBySlugAsync("shop-the-bedroom", Arg.Any<CancellationToken>())
            .Returns(collection);

        var result = await CreateService().GetCollectionAsync("shop-the-bedroom");

        result.IsSuccess.ShouldBeTrue();
        result.Data!.Seo.CanonicalPath.ShouldBe("/collections/shop-the-bedroom");
        result.Data.Seo.Title.ShouldBe("Shop the Bedroom");
    }

    [Fact]
    public async Task Collection_products_are_scoped_to_that_collection_and_to_published()
    {
        ProductQuery? captured = null;
        _collections.GetBySlugAsync("eid", Arg.Any<CancellationToken>()).Returns(NewCollection(id: 42));
        _products.SearchAsync(
                Arg.Do<ProductQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(PagedList<Product>.Empty(1, 20));

        await CreateService().GetCollectionProductsAsync(
            "eid", new ProductQuery { Status = ProductStatus.Draft });

        captured!.CollectionId.ShouldBe(42);
        captured.Status.ShouldBe(ProductStatus.Active);
    }

    // -------------------------------------------------------------------------
    // Card pricing
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_card_shows_the_cheapest_buyable_variant_not_the_base_price()
    {
        // "from ৳38,000" has to be a price a customer can actually pay. Showing
        // the base price would advertise 45,000 on a product whose cheapest
        // variant is 38,000.
        var product = NewProduct(basePrice: 45_000m);
        product.Variants =
        [
            NewVariant(product, "WH-A", priceOverride: 38_000m),
            NewVariant(product, "WH-B", priceOverride: 52_000m)
        ];

        _products.GetBySlugWithDetailsAsync("segun-king-bed", Arg.Any<CancellationToken>())
            .Returns(product);

        var result = await CreateService().GetProductAsync("segun-king-bed");

        result.Data!.FromPrice.ShouldBe(38_000m);
    }

    [Fact]
    public async Task An_inactive_variant_is_never_offered_and_never_sets_the_price()
    {
        var product = NewProduct(basePrice: 45_000m);
        var discontinued = NewVariant(product, "WH-OLD", priceOverride: 10_000m);
        discontinued.IsActive = false;

        product.Variants = [discontinued, NewVariant(product, "WH-NEW", priceOverride: 45_000m)];

        _products.GetBySlugWithDetailsAsync("segun-king-bed", Arg.Any<CancellationToken>())
            .Returns(product);

        var result = await CreateService().GetProductAsync("segun-king-bed");

        result.Data!.FromPrice.ShouldBe(45_000m);
        result.Data.Variants.Count.ShouldBe(1);
        result.Data.Variants.Single().Sku.ShouldBe("WH-NEW");
    }

    [Fact]
    public async Task Discount_percent_is_computed_from_the_price_actually_shown()
    {
        var product = NewProduct(basePrice: 45_000m);
        product.CompareAtPrice = Money.Taka(60_000m);
        product.Variants = [NewVariant(product, "WH-A")];

        _products.GetBySlugWithDetailsAsync("segun-king-bed", Arg.Any<CancellationToken>())
            .Returns(product);

        var result = await CreateService().GetProductAsync("segun-king-bed");

        result.Data!.IsOnOffer.ShouldBeTrue();
        result.Data.DiscountPercent.ShouldBe(25);
    }

    [Fact]
    public async Task No_compare_price_means_no_offer_badge()
    {
        var product = NewProduct();
        product.Variants = [NewVariant(product, "WH-A")];
        _products.GetBySlugWithDetailsAsync("segun-king-bed", Arg.Any<CancellationToken>())
            .Returns(product);

        var result = await CreateService().GetProductAsync("segun-king-bed");

        result.Data!.IsOnOffer.ShouldBeFalse();
        result.Data.DiscountPercent.ShouldBeNull();
    }

    [Fact]
    public async Task Lead_time_is_only_sent_for_made_to_order_products()
    {
        // Otherwise a stocked product sitting in the warehouse advertises
        // "ships in 14 days".
        var stocked = NewProduct();
        stocked.ProductType = ProductType.Stocked;
        stocked.LeadTimeDays = 14;
        stocked.Variants = [NewVariant(stocked, "WH-A")];

        _products.GetBySlugWithDetailsAsync("segun-king-bed", Arg.Any<CancellationToken>())
            .Returns(stocked);

        var result = await CreateService().GetProductAsync("segun-king-bed");

        result.Data!.LeadTimeDays.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // SEO
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Seo_title_falls_back_to_the_product_name()
    {
        // An empty title tag is the single worst thing a product page can do for
        // its own ranking, and nothing surfaces it until Search Console does.
        var product = NewProduct();
        product.SeoTitle = null;
        product.ShortDescription = LocalizedText.Create("Solid Segun wood, hand finished.");
        product.Variants = [NewVariant(product, "WH-A")];

        _products.GetBySlugWithDetailsAsync("segun-king-bed", Arg.Any<CancellationToken>())
            .Returns(product);

        var result = await CreateService().GetProductAsync("segun-king-bed");

        result.Data!.Seo.Title.ShouldBe("Segun King Bed");
        result.Data.Seo.Description.ShouldBe("Solid Segun wood, hand finished.");
        result.Data.Seo.CanonicalPath.ShouldBe("/products/segun-king-bed");
    }

    [Fact]
    public async Task Breadcrumbs_follow_the_ancestor_chain_root_first()
    {
        var product = NewProduct();
        product.Variants = [NewVariant(product, "WH-A")];
        _products.GetBySlugWithDetailsAsync("segun-king-bed", Arg.Any<CancellationToken>())
            .Returns(product);

        _categories.GetAncestorsAsync("/2/20/", Arg.Any<CancellationToken>()).Returns(
        [
            new Category { Id = 2, Name = LocalizedText.Create("Bedroom"), Slug = Slug.From("bedroom") },
            new Category { Id = 20, Name = LocalizedText.Create("Beds"), Slug = Slug.From("beds") }
        ]);

        var result = await CreateService().GetProductAsync("segun-king-bed");

        result.Data!.Breadcrumbs.Count.ShouldBe(2);
        result.Data.Breadcrumbs[0].Slug.ShouldBe("bedroom");
        result.Data.Breadcrumbs[1].Slug.ShouldBe("beds");
    }

    // -------------------------------------------------------------------------

    private static Product NewProduct(decimal basePrice = 45_000m) => new()
    {
        Id = 1,
        Code = "WH-BED-001",
        Name = LocalizedText.Create("Segun King Bed", "সেগুন কিং বেড"),
        Slug = Slug.From("segun-king-bed"),
        CategoryId = 20,
        Category = new Category
        {
            Id = 20,
            Name = LocalizedText.Create("Beds"),
            Slug = Slug.From("beds"),
            MaterializedPath = "/2/20/"
        },
        BasePrice = Money.Taka(basePrice),
        Status = ProductStatus.Active,
        SeoTitle = "Segun King Bed"
    };

    private static ProductVariant NewVariant(
        Product product, string sku, decimal? priceOverride = null) => new()
        {
            ProductId = product.Id,
            Product = product,
            Sku = sku,
            VariantName = sku,
            IsActive = true,
            PriceOverride = priceOverride is null ? null : Money.Taka(priceOverride.Value)
        };

    private static Collection NewCollection(long id = 1) => new()
    {
        Id = id,
        Name = LocalizedText.Create("Shop the Bedroom"),
        Slug = Slug.From("shop-the-bedroom"),
        IsActive = true
    };
}
