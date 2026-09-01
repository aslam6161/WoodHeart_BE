using WoodHeart.Repository;
using WoodHeart.Repository.Queries;
using WoodHeart.Service.DTOs.Catalog;

namespace WoodHeart.Service.Interfaces.Catalog;

public interface ICategoryService
{
    Task<GeneralResponse<IReadOnlyList<CategoryTreeDto>>> GetTreeAsync(
        bool includeInactive = false, CancellationToken cancellationToken = default);

    Task<GeneralResponse<CategoryDto>> GetByIdAsync(
        long id, CancellationToken cancellationToken = default);

    Task<GeneralResponse<CategoryDto>> GetBySlugAsync(
        string slug, CancellationToken cancellationToken = default);

    Task<GeneralResponse<CategoryDto>> CreateAsync(
        CreateCategoryDto dto, CancellationToken cancellationToken = default);

    Task<GeneralResponse<CategoryDto>> UpdateAsync(
        long id, UpdateCategoryDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-parents a category and rewrites the materialized path of every
    /// descendant.
    /// </summary>
    Task<GeneralResponse<CategoryDto>> MoveAsync(
        long id, MoveCategoryDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes a leaf category. Refuses while it still has children or
    /// products rather than orphaning them.
    /// </summary>
    Task<GeneralResponse> DeleteAsync(long id, CancellationToken cancellationToken = default);
}

public interface IBrandService
{
    Task<GeneralResponse<IReadOnlyList<BrandDto>>> GetAllAsync(
        bool includeInactive = false, CancellationToken cancellationToken = default);

    Task<GeneralResponse<BrandDto>> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<GeneralResponse<BrandDto>> CreateAsync(
        CreateBrandDto dto, CancellationToken cancellationToken = default);

    Task<GeneralResponse<BrandDto>> UpdateAsync(
        long id, UpdateBrandDto dto, CancellationToken cancellationToken = default);

    Task<GeneralResponse> DeleteAsync(long id, CancellationToken cancellationToken = default);
}

public interface IProductService
{
    Task<GeneralResponse<PagedList<ProductListItemDto>>> SearchAsync(
        ProductQuery query, CancellationToken cancellationToken = default);

    Task<GeneralResponse<ProductDetailDto>> GetByIdAsync(
        long id, CancellationToken cancellationToken = default);

    Task<GeneralResponse<ProductDetailDto>> GetBySlugAsync(
        string slug, CancellationToken cancellationToken = default);

    Task<GeneralResponse<ProductDetailDto>> CreateAsync(
        CreateProductDto dto, CancellationToken cancellationToken = default);

    Task<GeneralResponse<ProductDetailDto>> UpdateAsync(
        long id, UpdateProductDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes or withdraws a product. Separate from the general update so
    /// going live is a deliberate act rather than a side effect of saving.
    /// </summary>
    Task<GeneralResponse<ProductDetailDto>> ChangeStatusAsync(
        long id, ChangeProductStatusDto dto, CancellationToken cancellationToken = default);

    Task<GeneralResponse> DeleteAsync(long id, CancellationToken cancellationToken = default);

    Task<GeneralResponse<ProductVariantDto>> AddVariantAsync(
        long productId, CreateProductVariantDto dto, CancellationToken cancellationToken = default);

    Task<GeneralResponse<ProductVariantDto>> UpdateVariantAsync(
        long variantId, UpdateProductVariantDto dto, CancellationToken cancellationToken = default);

    /// <summary>Removes a variant. Refuses when it is the product's last one.</summary>
    Task<GeneralResponse> DeleteVariantAsync(
        long variantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The public catalog. Only ever returns published products — see
/// <c>StorefrontService</c> for why that is enforced rather than defaulted.
/// </summary>
/// <summary>
/// Photography and video attached to a product.
/// </summary>
/// <remarks>
/// Separate from <see cref="IProductService"/> because it is a separate job
/// with a separate failure mode: everything here talks to an external store
/// that can be slow, unreachable or unconfigured, and none of that should be
/// able to stop someone editing a price.
/// </remarks>
public interface IProductMediaService
{
    Task<GeneralResponse<IReadOnlyList<ProductMediaDto>>> GetAsync(
        long productId, CancellationToken cancellationToken = default);

    Task<GeneralResponse<ProductMediaDto>> UploadImageAsync(
        long productId, UploadProductImageDto dto, CancellationToken cancellationToken = default);

    /// <summary>Signs a ticket for a browser to upload one video directly.</summary>
    Task<GeneralResponse<VideoUploadTicketDto>> CreateVideoTicketAsync(
        long productId, CancellationToken cancellationToken = default);

    /// <summary>Verifies a directly-uploaded video with storage, then records it.</summary>
    Task<GeneralResponse<ProductMediaDto>> ConfirmVideoAsync(
        long productId, ConfirmVideoUploadDto dto, CancellationToken cancellationToken = default);

    Task<GeneralResponse<ProductMediaDto>> UpdateAsync(
        long productId,
        long mediaId,
        UpdateProductMediaDto dto,
        CancellationToken cancellationToken = default);

    Task<GeneralResponse<ProductMediaDto>> SetPrimaryAsync(
        long productId, long mediaId, CancellationToken cancellationToken = default);

    Task<GeneralResponse<IReadOnlyList<ProductMediaDto>>> ReorderAsync(
        long productId, ReorderProductMediaDto dto, CancellationToken cancellationToken = default);

    Task<GeneralResponse> DeleteAsync(
        long productId, long mediaId, CancellationToken cancellationToken = default);
}

public interface IStorefrontService
{
    Task<GeneralResponse<IReadOnlyList<CategoryTreeDto>>> GetCategoryTreeAsync(
        CancellationToken cancellationToken = default);

    Task<GeneralResponse<PagedList<StorefrontProductDto>>> SearchAsync(
        ProductQuery query, CancellationToken cancellationToken = default);

    Task<GeneralResponse<StorefrontProductDetailDto>> GetProductAsync(
        string slug, CancellationToken cancellationToken = default);

    Task<GeneralResponse<IReadOnlyList<StorefrontProductDto>>> GetRelatedAsync(
        string slug, CancellationToken cancellationToken = default);

    Task<GeneralResponse<StorefrontCollectionDto>> GetCollectionAsync(
        string slug, CancellationToken cancellationToken = default);

    Task<GeneralResponse<PagedList<StorefrontProductDto>>> GetCollectionProductsAsync(
        string slug, ProductQuery query, CancellationToken cancellationToken = default);
}
