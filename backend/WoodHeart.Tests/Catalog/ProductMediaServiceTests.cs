using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.Enums.Catalog;
using WoodHeart.Domain.Settings;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Catalog;
using WoodHeart.Service.DTOs.Catalog;
using WoodHeart.Service.Interfaces.Media;
using WoodHeart.Service.Services.Catalog;

namespace WoodHeart.Tests.Catalog;

/// <summary>
/// The media service's rules: what is refused, what becomes the hero image,
/// and what happens to the stored asset when the row does not survive.
/// </summary>
/// <remarks>
/// The <i>ordering</i> half of the primary-swap rule cannot be tested here —
/// it is a constraint the database enforces, and a substituted repository has
/// no constraints. That belongs to <c>ProductMediaDatabaseTests</c>, which runs
/// against a real Postgres.
/// </remarks>
public class ProductMediaServiceTests
{
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IProductVariantRepository _variants = Substitute.For<IProductVariantRepository>();
    private readonly IProductMediaRepository _media = Substitute.For<IProductMediaRepository>();
    private readonly IMediaStorage _storage = Substitute.For<IMediaStorage>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private const long ProductId = 7;

    public ProductMediaServiceTests()
    {
        _unitOfWork
            .ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task>>()(CancellationToken.None));

        _products.AnyAsync(Arg.Any<System.Linq.Expressions.Expression<Func<Product, bool>>>(),
            Arg.Any<CancellationToken>()).Returns(true);

        _media.MaxSortOrderAsync(ProductId, Arg.Any<CancellationToken>()).Returns(-1);

        _storage.IsConfigured.Returns(true);
        _storage.UploadImageAsync(Arg.Any<MediaUpload>(), Arg.Any<CancellationToken>())
            .Returns(GeneralResponse<StoredMedia>.Success(new StoredMedia
            {
                PublicId = "woodheart-test/products/7/abc123",
                MediaType = MediaType.Image,
                Width = 2000,
                Height = 1500,
                Bytes = 480_000,
                Format = "jpg"
            }));
    }

    private ProductMediaService CreateService() =>
        new(_products,
            _variants,
            _media,
            _storage,
            _unitOfWork,
            Options.Create(new CloudinarySettings
            {
                CloudName = "test",
                ApiKey = "key",
                ApiSecret = "secret",
                Folder = "woodheart-test",
                MaxImageBytes = 1_000_000
            }),
            NullLogger<ProductMediaService>.Instance);

    private static UploadProductImageDto Jpeg(long length = 1000, bool primary = false) => new()
    {
        // A real JPEG signature, because the service sniffs the bytes rather
        // than believing the file name.
        Content = new MemoryStream([0xFF, 0xD8, 0xFF, 0xE0, .. new byte[32]]),
        FileName = "bed.jpg",
        Length = length,
        AltText = "A segun bed against a white wall",
        IsPrimary = primary
    };

    // -------------------------------------------------------------------------
    // What is refused, and before what
    // -------------------------------------------------------------------------

    [Fact]
    public async Task An_oversized_image_is_refused_without_being_uploaded()
    {
        var result = await CreateService().UploadImageAsync(ProductId, Jpeg(length: 5_000_000));

        result.ErrorCode.ShouldBe(CatalogErrors.MediaTooLarge);

        // The point of checking first: a file over the limit must not cost a
        // multi-megabyte round trip to Cloudinary before being rejected.
        await _storage.DidNotReceive()
            .UploadImageAsync(Arg.Any<MediaUpload>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_file_that_is_not_an_image_is_refused_whatever_it_is_called()
    {
        var dto = new UploadProductImageDto
        {
            Content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<?php echo 1; ?>")),
            FileName = "innocent.jpg",
            Length = 16,
            AltText = "Not really a photograph"
        };

        var result = await CreateService().UploadImageAsync(ProductId, dto);

        result.ErrorCode.ShouldBe(CatalogErrors.MediaUnsupportedFormat);

        await _storage.DidNotReceive()
            .UploadImageAsync(Arg.Any<MediaUpload>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_variant_belonging_to_another_product_is_refused()
    {
        _variants.AnyAsync(
                Arg.Any<System.Linq.Expressions.Expression<Func<ProductVariant, bool>>>(),
                Arg.Any<CancellationToken>())
            .Returns(false);

        var dto = new UploadProductImageDto
        {
            Content = new MemoryStream([0xFF, 0xD8, 0xFF]),
            FileName = "bed.jpg",
            Length = 100,
            AltText = "A bed",
            VariantId = 999
        };

        var result = await CreateService().UploadImageAsync(ProductId, dto);

        // Attaching a photograph to another product's variant would put it on a
        // page it has nothing to do with.
        result.ErrorCode.ShouldBe(CatalogErrors.MediaVariantMismatch);
    }

    [Fact]
    public async Task Media_from_another_product_cannot_be_reached_by_id()
    {
        // The repository is scoped by both ids, so a media id that belongs
        // elsewhere simply is not found. This is the guard against editing one
        // product and deleting another product's hero image.
        _media.GetForProductAsync(ProductId, 42, Arg.Any<CancellationToken>())
            .Returns((ProductMedia?)null);

        var result = await CreateService().DeleteAsync(ProductId, 42);

        result.ErrorCode.ShouldBe(CatalogErrors.MediaNotFound);
    }

    // -------------------------------------------------------------------------
    // The hero image
    // -------------------------------------------------------------------------

    [Fact]
    public async Task The_first_image_becomes_primary_even_when_not_asked_for()
    {
        _media.GetPrimaryAsync(ProductId, Arg.Any<CancellationToken>())
            .Returns((ProductMedia?)null);

        var result = await CreateService().UploadImageAsync(ProductId, Jpeg(primary: false));

        // A product holding photographs with no hero renders a blank card in
        // every listing, and nothing reports it.
        result.IsSuccess.ShouldBeTrue();
        result.Data!.IsPrimary.ShouldBeTrue();
    }

    [Fact]
    public async Task A_later_image_is_not_primary_unless_asked_for()
    {
        _media.GetPrimaryAsync(ProductId, Arg.Any<CancellationToken>())
            .Returns(new ProductMedia { Id = 1, ProductId = ProductId, IsPrimary = true, StoragePath = "x" });

        var result = await CreateService().UploadImageAsync(ProductId, Jpeg(primary: false));

        result.Data!.IsPrimary.ShouldBeFalse();
    }

    [Fact]
    public async Task Promoting_an_image_stands_the_previous_one_down()
    {
        var current = new ProductMedia
        {
            Id = 1, ProductId = ProductId, IsPrimary = true, StoragePath = "old", MediaType = MediaType.Image
        };
        var incoming = new ProductMedia
        {
            Id = 2, ProductId = ProductId, IsPrimary = false, StoragePath = "new", MediaType = MediaType.Image
        };

        _media.GetForProductAsync(ProductId, 2, Arg.Any<CancellationToken>()).Returns(incoming);
        _media.GetPrimaryAsync(ProductId, Arg.Any<CancellationToken>()).Returns(current);

        var result = await CreateService().SetPrimaryAsync(ProductId, 2);

        result.IsSuccess.ShouldBeTrue();
        incoming.IsPrimary.ShouldBeTrue();
        current.IsPrimary.ShouldBeFalse();
    }

    [Fact]
    public async Task A_video_cannot_be_the_hero_image()
    {
        _media.GetForProductAsync(ProductId, 5, Arg.Any<CancellationToken>())
            .Returns(new ProductMedia
            {
                Id = 5, ProductId = ProductId, MediaType = MediaType.Video, StoragePath = "clip"
            });

        var result = await CreateService().SetPrimaryAsync(ProductId, 5);

        // A card showing a video where the photograph should be is a card
        // showing nothing.
        result.ErrorCode.ShouldBe(CatalogErrors.MediaUnsupportedFormat);
    }

    [Fact]
    public async Task Deleting_the_hero_promotes_the_next_image()
    {
        var hero = new ProductMedia
        {
            Id = 1, ProductId = ProductId, IsPrimary = true, StoragePath = "hero", MediaType = MediaType.Image
        };
        var successor = new ProductMedia
        {
            Id = 2, ProductId = ProductId, SortOrder = 1, StoragePath = "next", MediaType = MediaType.Image
        };

        _media.GetForProductAsync(ProductId, 1, Arg.Any<CancellationToken>()).Returns(hero);
        _media.GetPrimaryCandidateAsync(ProductId, 1, Arg.Any<CancellationToken>()).Returns(successor);
        _storage.DeleteAsync(Arg.Any<string>(), Arg.Any<MediaType>(), Arg.Any<CancellationToken>())
            .Returns(GeneralResponse.Success());

        var result = await CreateService().DeleteAsync(ProductId, 1);

        result.IsSuccess.ShouldBeTrue();

        // Otherwise removing the hero silently strips the product from every
        // listing that renders a card.
        successor.IsPrimary.ShouldBeTrue();
    }

    // -------------------------------------------------------------------------
    // Storage and the database staying in step
    // -------------------------------------------------------------------------

    [Fact]
    public async Task An_upload_whose_row_fails_to_save_is_destroyed_again()
    {
        _media.GetPrimaryAsync(ProductId, Arg.Any<CancellationToken>()).Returns((ProductMedia?)null);
        _media.InsertAsync(Arg.Any<ProductMedia>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("the database said no"));

        await Should.ThrowAsync<InvalidOperationException>(
            () => CreateService().UploadImageAsync(ProductId, Jpeg()));

        // Without this the bytes sit in Cloudinary billed forever, referenced
        // by nothing and findable by nobody.
        await _storage.Received(1).DeleteAsync(
            "woodheart-test/products/7/abc123", MediaType.Image, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deleting_succeeds_even_when_the_asset_cannot_be_destroyed()
    {
        var row = new ProductMedia
        {
            Id = 1, ProductId = ProductId, StoragePath = "hero", MediaType = MediaType.Image
        };

        _media.GetForProductAsync(ProductId, 1, Arg.Any<CancellationToken>()).Returns(row);
        _storage.DeleteAsync(Arg.Any<string>(), Arg.Any<MediaType>(), Arg.Any<CancellationToken>())
            .Returns(GeneralResponse.Fail(CatalogErrors.MediaStorageUnavailable, "down"));

        var result = await CreateService().DeleteAsync(ProductId, 1);

        // The row is already gone from every page. Reporting a failure for work
        // that succeeded would have an admin delete it again, and there is
        // nothing left to delete. The orphan is logged instead.
        result.IsSuccess.ShouldBeTrue();
    }

    // -------------------------------------------------------------------------
    // Direct video upload
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_public_id_outside_this_product_is_refused_without_asking_storage()
    {
        var result = await CreateService().ConfirmVideoAsync(ProductId, new ConfirmVideoUploadDto
        {
            PublicId = "woodheart-test/products/999/somebody-elses-video"
        });

        result.ErrorCode.ShouldBe(CatalogErrors.MediaNotUploaded);

        // A signed ticket is only ever issued for this product's folder, so a
        // confirm naming anything else is a client doing something it was never
        // handed permission for.
        await _storage.DidNotReceive()
            .GetAsync(Arg.Any<string>(), Arg.Any<MediaType>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_video_that_was_never_uploaded_is_refused()
    {
        _storage.GetAsync(Arg.Any<string>(), MediaType.Video, Arg.Any<CancellationToken>())
            .Returns(GeneralResponse<StoredMedia?>.Success(null));

        var result = await CreateService().ConfirmVideoAsync(ProductId, new ConfirmVideoUploadDto
        {
            PublicId = "woodheart-test/products/7/never-happened"
        });

        result.ErrorCode.ShouldBe(CatalogErrors.MediaNotUploaded);
    }

    [Fact]
    public async Task Confirmed_video_metadata_comes_from_storage_not_the_client()
    {
        _media.GetPrimaryAsync(ProductId, Arg.Any<CancellationToken>()).Returns((ProductMedia?)null);
        _storage.GetAsync("woodheart-test/products/7/clip", MediaType.Video, Arg.Any<CancellationToken>())
            .Returns(GeneralResponse<StoredMedia?>.Success(new StoredMedia
            {
                PublicId = "woodheart-test/products/7/clip",
                MediaType = MediaType.Video,
                Width = 1920,
                Height = 1080,
                Bytes = 40_000_000,
                Format = "mp4"
            }));

        var result = await CreateService().ConfirmVideoAsync(ProductId, new ConfirmVideoUploadDto
        {
            PublicId = "woodheart-test/products/7/clip"
        });

        result.IsSuccess.ShouldBeTrue();
        result.Data!.Width.ShouldBe(1920);
        result.Data.MediaType.ShouldBe(MediaType.Video);

        // A video is never promoted to hero, even as the product's first media.
        result.Data.IsPrimary.ShouldBeFalse();
    }
}
