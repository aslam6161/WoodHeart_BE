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
