using WoodHeart.Repository;
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
