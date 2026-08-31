using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.Enums.Catalog;
using WoodHeart.Domain.Helpers;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Catalog;
using WoodHeart.Repository.Queries;
using WoodHeart.Service.DTOs.Catalog;
using WoodHeart.Service.Interfaces.Catalog;
using WoodHeart.Service.Mapping.Catalog;

namespace WoodHeart.Service.Services.Catalog;

/// <summary>
/// The public catalog. Everything an anonymous visitor can read.
/// </summary>
/// <remarks>
/// <para>
/// <b>This service has one job that matters more than the rest: never serve a
/// product that is not published.</b> A draft is half-written copy and a price
/// nobody has approved. It must not be reachable by guessing a slug, by passing
/// <c>?status=Draft</c>, or by being linked from a collection somebody curated
/// before the product was finished.
/// </para>
/// <para>
/// So the status filter is <b>overwritten, not defaulted</b>. Every query the
/// caller supplies has <c>Status</c> forced to <see cref="ProductStatus.Active"/>
/// after binding, and the by-slug reads re-check visibility on the loaded entity.
/// A default would be a filter the caller can turn off.
/// </para>
/// </remarks>
public class StorefrontService(
    IProductRepository products,
    ICategoryRepository categories,
    ICollectionRepository collections,
    IDateTimeProvider clock) : IStorefrontService
{
    private const int RelatedProductCount = 8;

    public async Task<GeneralResponse<IReadOnlyList<CategoryTreeDto>>> GetCategoryTreeAsync(
        CancellationToken cancellationToken = default)
    {
        // includeInactive: false is not negotiable here. An inactive category is
        // one an admin has taken off the site.
        var flat = await categories.GetTreeAsync(includeInactive: false, cancellationToken);
        var counts = await categories.GetProductCountsAsync(cancellationToken);

        var dtos = flat.Select(c =>
        {
            var dto = CatalogMapper.ToTreeDto(c);
            dto.ProductCount = counts.GetValueOrDefault(c.Id);
            return dto;
        }).ToList();

        return GeneralResponse<IReadOnlyList<CategoryTreeDto>>.Success(
            CategoryService.BuildPublicHierarchy(dtos));
    }

    public async Task<GeneralResponse<PagedList<StorefrontProductDto>>> SearchAsync(
        ProductQuery query, CancellationToken cancellationToken = default)
    {
        var page = await products.SearchAsync(PublicOnly(query), cancellationToken);

        var mapped = new PagedList<StorefrontProductDto>(
            page.Select(CatalogMapper.ToStorefront).ToList(),
            page.TotalCount,
            page.CurrentPage,
            page.PageSize);

        return GeneralResponse<PagedList<StorefrontProductDto>>.Success(mapped);
    }

    public async Task<GeneralResponse<StorefrontProductDetailDto>> GetProductAsync(
        string slug, CancellationToken cancellationToken = default)
    {
        var product = await products.GetBySlugWithDetailsAsync(slug, cancellationToken);

        // One branch for "no such product" and "not published", returning the
        // same 404. Distinguishing them would confirm to anyone guessing slugs
        // that a product exists but is not live yet — which is exactly the
        // information a competitor wants before a launch.
        if (product is null || !product.IsPubliclyVisible)
        {
            return NotFound(slug);
        }

        var ancestors = await categories.GetAncestorsAsync(
            product.Category.MaterializedPath, cancellationToken);

        return GeneralResponse<StorefrontProductDetailDto>.Success(
            CatalogMapper.ToStorefrontDetail(product, ancestors));
    }

    public async Task<GeneralResponse<IReadOnlyList<StorefrontProductDto>>> GetRelatedAsync(
        string slug, CancellationToken cancellationToken = default)
    {
        var product = await products.GetBySlugWithDetailsAsync(slug, cancellationToken);

        if (product is null || !product.IsPubliclyVisible)
        {
            return GeneralResponse<IReadOnlyList<StorefrontProductDto>>.Fail(
                CatalogErrors.ProductNotFound, $"No product with the slug '{slug}'.");
        }

        var related = await products.GetRelatedAsync(
            product.Id, product.CategoryId, RelatedProductCount, cancellationToken);

        return GeneralResponse<IReadOnlyList<StorefrontProductDto>>.Success(
            [.. related.Select(CatalogMapper.ToStorefront)]);
    }

    public async Task<GeneralResponse<StorefrontCollectionDto>> GetCollectionAsync(
        string slug, CancellationToken cancellationToken = default)
    {
        var collection = await collections.GetBySlugAsync(slug, cancellationToken);

        // IsLiveAt takes the clock's reading rather than reading a clock itself,
        // which is what makes "the Eid collection goes live next Tuesday"
        // testable instead of something you find out on Tuesday.
        if (collection is null || !collection.IsLiveAt(clock.UtcNow))
        {
            return GeneralResponse<StorefrontCollectionDto>.Fail(
                CatalogErrors.CollectionNotFound, $"No collection with the slug '{slug}'.");
        }

        return GeneralResponse<StorefrontCollectionDto>.Success(
            CatalogMapper.ToStorefront(collection));
    }

    public async Task<GeneralResponse<PagedList<StorefrontProductDto>>> GetCollectionProductsAsync(
        string slug, ProductQuery query, CancellationToken cancellationToken = default)
    {
        var collection = await collections.GetBySlugAsync(slug, cancellationToken);

        if (collection is null || !collection.IsLiveAt(clock.UtcNow))
        {
            return GeneralResponse<PagedList<StorefrontProductDto>>.Fail(
                CatalogErrors.CollectionNotFound, $"No collection with the slug '{slug}'.");
        }

        var scoped = PublicOnly(query);
        scoped.CollectionId = collection.Id;

        var page = await products.SearchAsync(scoped, cancellationToken);

        var mapped = new PagedList<StorefrontProductDto>(
            page.Select(CatalogMapper.ToStorefront).ToList(),
            page.TotalCount,
            page.CurrentPage,
            page.PageSize);

        return GeneralResponse<PagedList<StorefrontProductDto>>.Success(mapped);
    }

    // -------------------------------------------------------------------------

    /// <summary>
    /// Forces a caller-supplied query down to what the public may see.
    /// </summary>
    /// <remarks>
    /// Overwrites rather than defaults, and returns a copy rather than mutating
    /// the bound model. <c>?status=Draft</c> in the query string binds happily,
    /// and the only thing standing between that and an unpublished price list
    /// is this method.
    /// </remarks>
    private static ProductQuery PublicOnly(ProductQuery query) => new()
    {
        PageNumber = query.PageNumber,
        PageSize = query.PageSize,
        CategoryId = query.CategoryId,
        IncludeDescendantCategories = query.IncludeDescendantCategories,
        BrandId = query.BrandId,
        CollectionId = query.CollectionId,
        ProductType = query.ProductType,
        IsFeatured = query.IsFeatured,
        Search = query.Search,
        MinPrice = query.MinPrice,
        MaxPrice = query.MaxPrice,
        SortBy = query.SortBy,
        Status = ProductStatus.Active
    };

    private static GeneralResponse<StorefrontProductDetailDto> NotFound(string slug) =>
        GeneralResponse<StorefrontProductDetailDto>.Fail(
            CatalogErrors.ProductNotFound, $"No product with the slug '{slug}'.");
}
