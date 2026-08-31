using NSubstitute;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.Enums.Catalog;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Catalog;
using WoodHeart.Service.DTOs.Catalog;
using WoodHeart.Service.Services.Catalog;
using WoodHeart.Tests.Helper;

namespace WoodHeart.Tests.Catalog;

/// <summary>
/// The product service's two invariants — a product always has at least one
/// variant, and exactly one of them is the default — plus publishing.
/// </summary>
public class ProductServiceTests
{
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IProductVariantRepository _variants = Substitute.For<IProductVariantRepository>();
    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();
    private readonly IBrandRepository _brands = Substitute.For<IBrandRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeClock _clock = new();

    public ProductServiceTests()
    {
        // The transaction wrapper has to actually invoke its callback, or every
        // method that commits returns null. Both generic arities are used.
        _unitOfWork
            .ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<GeneralResponse<ProductVariantDto>>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<GeneralResponse<ProductVariantDto>>>>()(
                CancellationToken.None));

        _unitOfWork
            .ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task>>()(CancellationToken.None));

        // A valid category by default; individual tests override.
        _categories.GetByIdAsync(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new Category
            {
                Id = 1,
                Name = LocalizedText.Create("Beds"),
                Slug = Slug.From("beds"),
                MaterializedPath = "/1/"
            });
    }

    private ProductService CreateService() =>
        new(_products, _variants, _categories, _brands, _clock, _unitOfWork);

    // -------------------------------------------------------------------------
    // "Every product has at least one variant"
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Creating_a_product_with_no_variants_still_produces_one()
    {
        // The invariant that gives the catalog its shape. A product with no
        // variants has no price and no stock — it cannot be sold, and nothing
        // reports that it cannot.
        Product? captured = null;
        await _products.InsertAsync(Arg.Do<Product>(p => captured = p), Arg.Any<CancellationToken>());

        var result = await CreateService().CreateAsync(NewProductDto());

        result.IsSuccess.ShouldBeTrue();
        captured.ShouldNotBeNull();
        captured!.Variants.Count.ShouldBe(1);
        captured.Variants.Single().IsDefault.ShouldBeTrue();
        captured.Variants.Single().Sku.ShouldBe("WH-BED-001");
    }

    [Fact]
    public async Task Deleting_the_last_variant_is_refused()
    {
        var variant = NewVariant(1, "WH-A", isDefault: true);
        _variants.GetByIdAsync(1L, Arg.Any<CancellationToken>()).Returns(variant);
        _variants.GetByProductAsync(1, Arg.Any<CancellationToken>()).Returns([variant]);

        var result = await CreateService().DeleteVariantAsync(1);

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(CatalogErrors.LastVariant);
        _variants.DidNotReceive().Delete(Arg.Any<ProductVariant>());
    }

    [Fact]
    public async Task Deleting_the_default_variant_promotes_a_successor()
    {
        // Otherwise the product is left with no default, the product page has
        // nothing selected, and it shows no price at all.
        var defaultVariant = NewVariant(1, "WH-A", isDefault: true, sortOrder: 0);
        var successor = NewVariant(2, "WH-B", sortOrder: 1);
        var third = NewVariant(3, "WH-C", sortOrder: 2);

        _variants.GetByIdAsync(1L, Arg.Any<CancellationToken>()).Returns(defaultVariant);
        _variants.GetByProductAsync(1, Arg.Any<CancellationToken>())
            .Returns([defaultVariant, successor, third]);

        var result = await CreateService().DeleteVariantAsync(1);

        result.IsSuccess.ShouldBeTrue();
        _variants.Received(1).Delete(defaultVariant);

        // The lowest remaining sort order, not an arbitrary one.
        successor.IsDefault.ShouldBeTrue();
        third.IsDefault.ShouldBeFalse();
    }

    [Fact]
    public async Task Deleting_a_non_default_variant_leaves_the_default_alone()
    {
        var defaultVariant = NewVariant(1, "WH-A", isDefault: true, sortOrder: 0);
        var other = NewVariant(2, "WH-B", sortOrder: 1);

        _variants.GetByIdAsync(2L, Arg.Any<CancellationToken>()).Returns(other);
        _variants.GetByProductAsync(1, Arg.Any<CancellationToken>()).Returns([defaultVariant, other]);

        var result = await CreateService().DeleteVariantAsync(2);

        result.IsSuccess.ShouldBeTrue();
        defaultVariant.IsDefault.ShouldBeTrue();
    }

    // -------------------------------------------------------------------------
    // "Exactly one default"
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Create_flags_the_first_variant_default_when_none_is_flagged()
    {
        Product? captured = null;
        await _products.InsertAsync(Arg.Do<Product>(p => captured = p), Arg.Any<CancellationToken>());

        var dto = NewProductDto();
        dto.Variants =
        [
            new CreateProductVariantDto { Sku = "WH-1" },
            new CreateProductVariantDto { Sku = "WH-2" }
        ];

        await CreateService().CreateAsync(dto);

        captured!.Variants.Count(v => v.IsDefault).ShouldBe(1);
        captured.Variants.First().Sku.ShouldBe("WH-1");
        captured.Variants.First().IsDefault.ShouldBeTrue();
    }

    [Fact]
    public async Task Create_reduces_multiple_flagged_defaults_to_one()
    {
        // A filtered unique index enforces this at the database. Reaching it
        // produces a Postgres constraint error, not a message — so the service
        // resolves it first rather than rejecting the whole product over a
        // presentation detail.
        Product? captured = null;
        await _products.InsertAsync(Arg.Do<Product>(p => captured = p), Arg.Any<CancellationToken>());

        var dto = NewProductDto();
        dto.Variants =
        [
            new CreateProductVariantDto { Sku = "WH-1", IsDefault = true },
            new CreateProductVariantDto { Sku = "WH-2", IsDefault = true }
        ];

        await CreateService().CreateAsync(dto);

        captured!.Variants.Count(v => v.IsDefault).ShouldBe(1);
    }

    [Fact]
    public async Task Adding_a_default_variant_clears_the_previous_one()
    {
        var existing = NewVariant(1, "WH-A", isDefault: true);
        _products.GetByIdAsync(1L, Arg.Any<CancellationToken>()).Returns(NewProduct());
        _variants.GetDefaultAsync(1, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await CreateService().AddVariantAsync(
            1, new CreateProductVariantDto { Sku = "WH-B", IsDefault = true });

        result.IsSuccess.ShouldBeTrue();
        existing.IsDefault.ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // SKU and slug uniqueness
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Create_rejects_a_sku_duplicated_within_the_request()
    {
        // Caught before the database, so the message names the offending SKU
        // instead of surfacing a constraint violation mid-insert.
        var dto = NewProductDto();
        dto.Variants =
        [
            new CreateProductVariantDto { Sku = "WH-DUP" },
            new CreateProductVariantDto { Sku = "wh-dup" }
        ];

        var result = await CreateService().CreateAsync(dto);

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(CatalogErrors.VariantSkuTaken);
        await _products.DidNotReceive().InsertAsync(Arg.Any<Product>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_rejects_a_sku_already_used_by_another_product()
    {
        _variants.SkuExistsAsync("WH-TAKEN", null, Arg.Any<CancellationToken>()).Returns(true);

        var dto = NewProductDto();
        dto.Variants = [new CreateProductVariantDto { Sku = "WH-TAKEN" }];

        var result = await CreateService().CreateAsync(dto);

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(CatalogErrors.VariantSkuTaken);
    }

    [Fact]
    public async Task Create_rejects_a_taken_product_code()
    {
        _products.CodeExistsAsync("WH-BED-001", null, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateService().CreateAsync(NewProductDto());

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(CatalogErrors.ProductCodeTaken);
    }

    [Fact]
    public async Task Create_fails_when_the_category_does_not_exist()
    {
        _categories.GetByIdAsync(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns((Category?)null);

        var result = await CreateService().CreateAsync(NewProductDto());

        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(CatalogErrors.CategoryNotFound);
    }

    // -------------------------------------------------------------------------
    // Publishing
    // -------------------------------------------------------------------------

    [Fact]
    public async Task New_products_are_created_as_drafts()
    {
        // A product that appears on the storefront the instant it is saved gets
        // found half-written.
        Product? captured = null;
        await _products.InsertAsync(Arg.Do<Product>(p => captured = p), Arg.Any<CancellationToken>());

        await CreateService().CreateAsync(NewProductDto());

        captured!.Status.ShouldBe(ProductStatus.Draft);
        captured.PublishedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Publishing_stamps_published_at_from_the_injected_clock()
    {
        var product = NewProduct();
        _products.GetByIdWithDetailsAsync(1, Arg.Any<CancellationToken>()).Returns(product);

        var result = await CreateService().ChangeStatusAsync(
            1, new ChangeProductStatusDto { Status = ProductStatus.Active });

        result.IsSuccess.ShouldBeTrue();
        product.Status.ShouldBe(ProductStatus.Active);
        product.PublishedAt.ShouldBe(FakeClock.DefaultNow);
    }

    [Fact]
    public async Task Republishing_does_not_move_the_original_published_date()
    {
        // PublishedAt anchors "new arrivals" ordering and the sitemap. A product
        // that re-appears as new every time someone toggles it is a ranking
        // problem, not a display quirk.
        var firstPublished = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var product = NewProduct();
        product.PublishedAt = firstPublished;
        product.Status = ProductStatus.Archived;

        _products.GetByIdWithDetailsAsync(1, Arg.Any<CancellationToken>()).Returns(product);

        await CreateService().ChangeStatusAsync(
            1, new ChangeProductStatusDto { Status = ProductStatus.Active });

        product.PublishedAt.ShouldBe(firstPublished);
    }

    [Fact]
    public async Task Archiving_leaves_published_at_intact()
    {
        var product = NewProduct();
        product.PublishedAt = FakeClock.DefaultNow;
        product.Status = ProductStatus.Active;

        _products.GetByIdWithDetailsAsync(1, Arg.Any<CancellationToken>()).Returns(product);

        await CreateService().ChangeStatusAsync(
            1, new ChangeProductStatusDto { Status = ProductStatus.Archived });

        product.Status.ShouldBe(ProductStatus.Archived);
        product.PublishedAt.ShouldBe(FakeClock.DefaultNow);
    }

    // -------------------------------------------------------------------------
    // Variant naming
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Variant_name_is_built_from_the_options_when_none_is_given()
    {
        _products.GetByIdAsync(1L, Arg.Any<CancellationToken>()).Returns(NewProduct());

        var result = await CreateService().AddVariantAsync(1, new CreateProductVariantDto
        {
            Sku = "WH-SEGUN-6",
            OptionValues = new Dictionary<string, string> { ["Wood"] = "Segun", ["Size"] = "6ft" }
        });

        result.IsSuccess.ShouldBeTrue();
        result.Data!.VariantName.ShouldBe("Segun · 6ft");
    }

    [Fact]
    public async Task An_explicit_variant_name_wins_over_the_generated_one()
    {
        _products.GetByIdAsync(1L, Arg.Any<CancellationToken>()).Returns(NewProduct());

        var result = await CreateService().AddVariantAsync(1, new CreateProductVariantDto
        {
            Sku = "WH-SEGUN-6",
            VariantName = "Segun King, 6 foot",
            OptionValues = new Dictionary<string, string> { ["Wood"] = "Segun" }
        });

        result.Data!.VariantName.ShouldBe("Segun King, 6 foot");
    }

    [Fact]
    public async Task Variant_without_a_price_override_reports_the_product_price()
    {
        _products.GetByIdAsync(1L, Arg.Any<CancellationToken>()).Returns(NewProduct(basePrice: 45_000m));

        var result = await CreateService().AddVariantAsync(
            1, new CreateProductVariantDto { Sku = "WH-A" });

        result.Data!.EffectivePrice.ShouldBe(45_000m);
    }

    // -------------------------------------------------------------------------

    private static CreateProductDto NewProductDto() => new()
    {
        Code = "WH-BED-001",
        NameEn = "Segun King Bed",
        NameBn = "সেগুন কিং বেড",
        CategoryId = 1,
        BasePrice = 45_000m
    };

    private static Product NewProduct(decimal basePrice = 45_000m) => new()
    {
        Id = 1,
        Code = "WH-BED-001",
        Name = LocalizedText.Create("Segun King Bed"),
        Slug = Slug.From("segun-king-bed"),
        CategoryId = 1,
        BasePrice = Money.Taka(basePrice),
        Status = ProductStatus.Draft
    };

    private static ProductVariant NewVariant(
        long id, string sku, bool isDefault = false, int sortOrder = 0) => new()
        {
            Id = id,
            ProductId = 1,
            Product = NewProduct(),
            Sku = sku,
            VariantName = sku,
            IsDefault = isDefault,
            SortOrder = sortOrder
        };
}
