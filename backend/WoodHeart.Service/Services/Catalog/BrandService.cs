using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Catalog;
using WoodHeart.Service.DTOs.Catalog;
using WoodHeart.Service.Interfaces.Catalog;
using WoodHeart.Service.Mapping.Catalog;

namespace WoodHeart.Service.Services.Catalog;

/// <summary>
/// Brand CRUD. Deliberately unremarkable — there is no tree and no hierarchy,
/// and the only rule worth enforcing is that a brand still carrying products
/// cannot be deleted out from under them.
/// </summary>
public class BrandService(
    IBrandRepository brands,
    IUnitOfWork unitOfWork) : IBrandService
{
    public async Task<GeneralResponse<IReadOnlyList<BrandDto>>> GetAllAsync(
        bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var all = await brands.GetAllAsync(includeInactive, cancellationToken);

        // One grouped query rather than a count per brand.
        var counts = await brands.GetProductCountsAsync(cancellationToken);

        var dtos = all.Select(b =>
        {
            var dto = CatalogMapper.ToDto(b);
            dto.ProductCount = counts.GetValueOrDefault(b.Id);
            return dto;
        }).ToList();

        return GeneralResponse<IReadOnlyList<BrandDto>>.Success(dtos);
    }

    public async Task<GeneralResponse<BrandDto>> GetByIdAsync(
        long id, CancellationToken cancellationToken = default)
    {
        var brand = await brands.GetByIdAsync(id, cancellationToken);

        return brand is null
            ? NotFound(id)
            : GeneralResponse<BrandDto>.Success(CatalogMapper.ToDto(brand));
    }

    public async Task<GeneralResponse<BrandDto>> CreateAsync(
        CreateBrandDto dto, CancellationToken cancellationToken = default)
    {
        if (!TryBuildSlug(dto.Slug, dto.NameEn, out var slug, out var failure))
        {
            return failure!;
        }

        if (await brands.SlugExistsAsync(slug!.Value, null, cancellationToken))
        {
            return SlugTaken(slug.Value);
        }

        var brand = new Brand
        {
            Name = LocalizedText.Create(dto.NameEn, dto.NameBn),
            Slug = slug,
            Description = string.IsNullOrWhiteSpace(dto.DescriptionEn)
                ? null
                : LocalizedText.Create(dto.DescriptionEn, dto.DescriptionBn),
            LogoPath = dto.LogoPath,
            IsActive = dto.IsActive,
            SortOrder = dto.SortOrder
        };

        await brands.InsertAsync(brand, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return GeneralResponse<BrandDto>.Success(CatalogMapper.ToDto(brand), "Brand created.", brand.Id);
    }

    public async Task<GeneralResponse<BrandDto>> UpdateAsync(
        long id, UpdateBrandDto dto, CancellationToken cancellationToken = default)
    {
        var brand = await brands.GetByIdAsync(id, cancellationToken);

        if (brand is null)
        {
            return NotFound(id);
        }

        // Same rule as categories: only move the slug when one was explicitly
        // supplied, because it is a published URL.
        if (!string.IsNullOrWhiteSpace(dto.Slug))
        {
            if (!TryBuildSlug(dto.Slug, dto.NameEn, out var slug, out var failure))
            {
                return failure!;
            }

            if (slug!.Value != brand.Slug.Value)
            {
                if (await brands.SlugExistsAsync(slug.Value, id, cancellationToken))
                {
                    return SlugTaken(slug.Value);
                }

                brand.Slug = slug;
            }
        }

        brand.Name = LocalizedText.Create(dto.NameEn, dto.NameBn);
        brand.Description = string.IsNullOrWhiteSpace(dto.DescriptionEn)
            ? null
            : LocalizedText.Create(dto.DescriptionEn, dto.DescriptionBn);
        brand.LogoPath = dto.LogoPath;
        brand.IsActive = dto.IsActive;
        brand.SortOrder = dto.SortOrder;

        brands.Update(brand);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return GeneralResponse<BrandDto>.Success(CatalogMapper.ToDto(brand), "Brand updated.", id);
    }

    public async Task<GeneralResponse> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var brand = await brands.GetByIdAsync(id, cancellationToken);

        if (brand is null)
        {
            return GeneralResponse.Fail(CatalogErrors.BrandNotFound, $"No brand with id {id}.");
        }

        // The foreign key is SetNull, so deleting would succeed and quietly
        // strip the brand from every product that had it. Refusing makes the
        // admin decide what those products should say instead.
        if (await brands.HasProductsAsync(id, cancellationToken))
        {
            return GeneralResponse.Fail(
                CatalogErrors.BrandHasProducts,
                "Products still reference this brand. Reassign them first, or deactivate the brand instead.");
        }

        brands.Delete(brand);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return GeneralResponse.Success("Brand deleted.", id);
    }

    // -------------------------------------------------------------------------

    private static bool TryBuildSlug(
        string? supplied, string fallback, out Slug? slug, out GeneralResponse<BrandDto>? failure)
    {
        try
        {
            slug = Slug.From(string.IsNullOrWhiteSpace(supplied) ? fallback : supplied);
            failure = null;

            return true;
        }
        catch (ArgumentException)
        {
            slug = null;
            failure = GeneralResponse<BrandDto>.Fail(
                CatalogErrors.SlugNotDerivable,
                "The name does not contain any characters usable in a URL. Supply a slug explicitly.");

            return false;
        }
    }

    private static GeneralResponse<BrandDto> NotFound(long id) =>
        GeneralResponse<BrandDto>.Fail(CatalogErrors.BrandNotFound, $"No brand with id {id}.");

    private static GeneralResponse<BrandDto> SlugTaken(string slug) =>
        GeneralResponse<BrandDto>.Fail(
            CatalogErrors.BrandSlugTaken, $"The slug '{slug}' is already in use.");
}
