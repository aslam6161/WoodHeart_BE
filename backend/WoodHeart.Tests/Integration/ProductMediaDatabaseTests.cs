using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.Enums.Catalog;
using WoodHeart.Domain.Settings;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Repositories.Catalog;
using WoodHeart.Service.DTOs.Catalog;
using WoodHeart.Service.Interfaces.Media;
using WoodHeart.Service.Services.Catalog;

namespace WoodHeart.Tests.Integration;

/// <summary>
/// The media rules that only a real PostgreSQL can check.
/// </summary>
/// <remarks>
/// <para>
/// <b>These exist because of one index.</b> <c>ux_product_media_one_primary</c>
/// is unique on <c>product_id</c>, filtered to
/// <c>is_primary = true AND is_deleted = false</c>. It is the thing that makes
/// "exactly one hero image" true rather than merely intended — and it is also a
/// tripwire, because any code path that briefly leaves two rows primary inside
/// one statement batch fails against Postgres and passes against a substitute.
/// </para>
/// <para>
/// Promoting a new hero and standing the old one down is exactly such a path.
/// Staged into a single <c>SaveChanges</c>, EF may emit the two <c>UPDATE</c>s
/// in either order, and one order violates the index. The service commits them
/// separately inside one transaction for that reason; these tests are what stop
/// someone tidying that back into one call.
/// </para>
/// </remarks>
public class ProductMediaDatabaseTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private readonly IMediaStorage _storage = Substitute.For<IMediaStorage>();

    /// <summary>Builds the service over real repositories and a real context.</summary>
    /// <remarks>
    /// Storage stays substituted. The point here is the database, and a test
    /// that needed Cloudinary credentials would not run anywhere.
    /// </remarks>
    private (ProductMediaService Service, DataContext Context) CreateService()
    {
        var context = fixture.CreateContext();

        _storage.IsConfigured.Returns(true);
        _storage.DeleteAsync(Arg.Any<string>(), Arg.Any<MediaType>(), Arg.Any<CancellationToken>())
            .Returns(GeneralResponse.Success());

        var service = new ProductMediaService(
            new ProductRepository(context),
            new ProductVariantRepository(context),
            new ProductMediaRepository(context),
            _storage,
            context,
            Options.Create(new CloudinarySettings { Folder = "woodheart-test" }),
            NullLogger<ProductMediaService>.Instance);

        return (service, context);
    }

    private static async Task<long> SeedProductAsync(DataContext context)
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
        await context.SaveChangesAsync();

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

        return product.Id;
    }

    private static async Task<ProductMedia> AddImageAsync(
        DataContext context, long productId, int sortOrder, bool isPrimary)
    {
        var row = new ProductMedia
        {
            ProductId = productId,
            MediaType = MediaType.Image,
            StoragePath = $"woodheart-test/products/{productId}/{Guid.NewGuid():n}",
            AltText = "A bed",
            IsPrimary = isPrimary,
            SortOrder = sortOrder
        };

        await context.ProductMedia.AddAsync(row);
        await context.SaveChangesAsync();

        return row;
    }

    // -------------------------------------------------------------------------

    [RequiresPostgresFact]
    public async Task The_index_really_does_refuse_a_second_primary()
    {
        // Proves the tripwire is armed. If this ever stops throwing, the index
        // has been dropped and every test below is checking nothing.
        var (_, context) = CreateService();
        await using var _ = context;

        var productId = await SeedProductAsync(context);
        await AddImageAsync(context, productId, sortOrder: 0, isPrimary: true);

        await Should.ThrowAsync<Exception>(
            () => AddImageAsync(context, productId, sortOrder: 1, isPrimary: true));
    }

    [RequiresPostgresFact]
    public async Task Promoting_a_new_hero_does_not_trip_the_unique_index()
    {
        var (service, context) = CreateService();
        await using var _ = context;

        var productId = await SeedProductAsync(context);
        var first = await AddImageAsync(context, productId, sortOrder: 0, isPrimary: true);
        var second = await AddImageAsync(context, productId, sortOrder: 1, isPrimary: false);

        var result = await service.SetPrimaryAsync(productId, second.Id);

        result.IsSuccess.ShouldBeTrue(result.Message);

        // Read back through a new context: an assertion against the tracked
        // entities would pass even if nothing reached the database.
        await using var verify = fixture.CreateContext();

        var primaries = verify.ProductMedia
            .Where(m => m.ProductId == productId && m.IsPrimary)
            .Select(m => m.Id)
            .ToList();

        primaries.ShouldBe([second.Id]);
        primaries.ShouldNotContain(first.Id);
    }

    [RequiresPostgresFact]
    public async Task Deleting_the_hero_promotes_the_next_image_without_tripping_the_index()
    {
        var (service, context) = CreateService();
        await using var _ = context;

        var productId = await SeedProductAsync(context);
        var hero = await AddImageAsync(context, productId, sortOrder: 0, isPrimary: true);
        var next = await AddImageAsync(context, productId, sortOrder: 1, isPrimary: false);

        var result = await service.DeleteAsync(productId, hero.Id);

        result.IsSuccess.ShouldBeTrue(result.Message);

        await using var verify = fixture.CreateContext();

        // The soft-deleted row is filtered out by the query filter, which is
        // also what takes it out of the index's scope.
        var remaining = verify.ProductMedia
            .Where(m => m.ProductId == productId)
            .ToList();

        remaining.Count.ShouldBe(1);
        remaining[0].Id.ShouldBe(next.Id);
        remaining[0].IsPrimary.ShouldBeTrue();
    }

    [RequiresPostgresFact]
    public async Task Deleting_the_only_image_leaves_the_product_with_none()
    {
        var (service, context) = CreateService();
        await using var _ = context;

        var productId = await SeedProductAsync(context);
        var only = await AddImageAsync(context, productId, sortOrder: 0, isPrimary: true);

        var result = await service.DeleteAsync(productId, only.Id);

        result.IsSuccess.ShouldBeTrue(result.Message);

        await using var verify = fixture.CreateContext();
        verify.ProductMedia.Count(m => m.ProductId == productId).ShouldBe(0);
    }

    [RequiresPostgresFact]
    public async Task Reordering_writes_a_contiguous_order()
    {
        var (service, context) = CreateService();
        await using var _ = context;

        var productId = await SeedProductAsync(context);
        var a = await AddImageAsync(context, productId, sortOrder: 0, isPrimary: true);
        var b = await AddImageAsync(context, productId, sortOrder: 1, isPrimary: false);
        var c = await AddImageAsync(context, productId, sortOrder: 2, isPrimary: false);

        var result = await service.ReorderAsync(
            productId, new ReorderProductMediaDto { MediaIds = [c.Id, a.Id, b.Id] });

        result.IsSuccess.ShouldBeTrue(result.Message);

        await using var verify = fixture.CreateContext();

        var order = verify.ProductMedia
            .Where(m => m.ProductId == productId)
            .OrderBy(m => m.SortOrder)
            .Select(m => m.Id)
            .ToList();

        order.ShouldBe([c.Id, a.Id, b.Id]);
    }

    [RequiresPostgresFact]
    public async Task Media_is_not_reachable_through_another_products_id()
    {
        var (service, context) = CreateService();
        await using var _ = context;

        var mine = await SeedProductAsync(context);
        var theirs = await SeedProductAsync(context);
        var theirHero = await AddImageAsync(context, theirs, sortOrder: 0, isPrimary: true);

        // The route is /products/{productId}/media/{mediaId} and both come from
        // the URL. Looking the row up by media id alone would delete another
        // product's hero image from an admin screen showing this one.
        var result = await service.DeleteAsync(mine, theirHero.Id);

        result.IsSuccess.ShouldBeFalse();

        await using var verify = fixture.CreateContext();
        verify.ProductMedia.Count(m => m.ProductId == theirs).ShouldBe(1);
    }
}
