using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.Enums.Catalog;
using WoodHeart.Domain.Helpers;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Catalog;
using WoodHeart.Repository.Queries;
using WoodHeart.Service.DTOs.Catalog;
using WoodHeart.Service.Interfaces.Catalog;
using WoodHeart.Service.Mapping.Catalog;

namespace WoodHeart.Service.Services.Catalog;

/// <summary>
/// Products and their variants.
/// </summary>
/// <remarks>
/// <para>
/// This service owns the invariant that gives the catalog its shape:
/// <b>a product always has at least one variant.</b> Create makes one when none
/// is supplied, and delete refuses to remove the last one. Everything
/// downstream — cart, stock, order lines — reads variants, so a product with
/// none is a product that cannot be sold and does not report that it cannot.
/// </para>
/// <para>
/// It also owns "exactly one variant is the default". The database enforces it
/// with a filtered unique index, but hitting that index produces a constraint
/// violation rather than a message, so the flag is cleared here before it is
/// set elsewhere.
/// </para>
/// </remarks>
public class ProductService(
    IProductRepository products,
    IProductVariantRepository variants,
    ICategoryRepository categories,
    IBrandRepository brands,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork) : IProductService
{
    public async Task<GeneralResponse<PagedList<ProductListItemDto>>> SearchAsync(
        ProductQuery query, CancellationToken cancellationToken = default)
    {
        var page = await products.SearchAsync(query, cancellationToken);

        var dtos = page.Select(CatalogMapper.ToListItem).ToList();

        var mapped = new PagedList<ProductListItemDto>(
            dtos, page.TotalCount, page.CurrentPage, page.PageSize);

        return GeneralResponse<PagedList<ProductListItemDto>>.Success(mapped);
    }

    public async Task<GeneralResponse<ProductDetailDto>> GetByIdAsync(
        long id, CancellationToken cancellationToken = default)
    {
        var product = await products.GetByIdWithDetailsAsync(id, cancellationToken);

        return product is null
            ? NotFound(id)
            : GeneralResponse<ProductDetailDto>.Success(CatalogMapper.ToDetail(product));
    }

    public async Task<GeneralResponse<ProductDetailDto>> GetBySlugAsync(
        string slug, CancellationToken cancellationToken = default)
    {
        var product = await products.GetBySlugWithDetailsAsync(slug, cancellationToken);

        return product is null
            ? GeneralResponse<ProductDetailDto>.Fail(
                CatalogErrors.ProductNotFound, $"No product with the slug '{slug}'.")
            : GeneralResponse<ProductDetailDto>.Success(CatalogMapper.ToDetail(product));
    }

    public async Task<GeneralResponse<ProductDetailDto>> CreateAsync(
        CreateProductDto dto, CancellationToken cancellationToken = default)
    {
        var validation = await ValidateReferencesAsync(dto.CategoryId, dto.BrandId, cancellationToken);

        if (validation is not null)
        {
            return validation;
        }

        if (!TryBuildSlug(dto.Slug, dto.NameEn, out var slug, out var slugFailure))
        {
            return slugFailure!;
        }

        if (await products.SlugExistsAsync(slug!.Value, null, cancellationToken))
        {
            return SlugTaken(slug.Value);
        }

        if (await products.CodeExistsAsync(dto.Code, null, cancellationToken))
        {
            return GeneralResponse<ProductDetailDto>.Fail(
                CatalogErrors.ProductCodeTaken, $"The product code '{dto.Code}' is already in use.");
        }

        // Every SKU across the whole catalog must be unique, so duplicates
        // within the request are caught before touching the database — a
        // constraint violation mid-insert produces a stack trace, not a message
        // naming the offending SKU.
        var skus = dto.Variants.Select(v => v.Sku).ToList();

        if (skus.Count != skus.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            return GeneralResponse<ProductDetailDto>.Fail(
                CatalogErrors.VariantSkuTaken, "The same SKU appears more than once in this request.");
        }

        foreach (var sku in skus)
        {
            if (await variants.SkuExistsAsync(sku, null, cancellationToken))
            {
                return GeneralResponse<ProductDetailDto>.Fail(
                    CatalogErrors.VariantSkuTaken, $"The SKU '{sku}' is already in use.");
            }
        }

        var product = new Product
        {
            Code = dto.Code.Trim(),
            Name = LocalizedText.Create(dto.NameEn, dto.NameBn),
            Slug = slug,
            ShortDescription = Optional(dto.ShortDescriptionEn, dto.ShortDescriptionBn),
            Description = Optional(dto.DescriptionEn, dto.DescriptionBn),
            CategoryId = dto.CategoryId,
            BrandId = dto.BrandId,
            ProductType = dto.ProductType,
            // Always Draft. Publishing is a separate, deliberate action — a
            // product that appears on the storefront the instant it is saved
            // gets found half-written.
            Status = ProductStatus.Draft,
            BasePrice = Money.Taka(dto.BasePrice),
            CompareAtPrice = dto.CompareAtPrice is { } compare ? Money.Taka(compare) : null,
            LengthCm = dto.LengthCm,
            WidthCm = dto.WidthCm,
            HeightCm = dto.HeightCm,
            WeightKg = dto.WeightKg,
            Material = dto.Material,
            FinishType = dto.FinishType,
            WarrantyMonths = dto.WarrantyMonths,
            LeadTimeDays = dto.LeadTimeDays,
            AssemblyRequired = dto.AssemblyRequired,
            DeliverySurcharge = dto.DeliverySurcharge is { } surcharge ? Money.Taka(surcharge) : null,
            IsFeatured = dto.IsFeatured,
            SeoTitle = dto.SeoTitle,
            SeoDescription = dto.SeoDescription
        };

        product.Variants = BuildVariants(dto, product);

        await products.InsertAsync(product, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var saved = await products.GetByIdWithDetailsAsync(product.Id, cancellationToken);

        return GeneralResponse<ProductDetailDto>.Success(
            CatalogMapper.ToDetail(saved ?? product), "Product created.", product.Id);
    }

    public async Task<GeneralResponse<ProductDetailDto>> UpdateAsync(
        long id, UpdateProductDto dto, CancellationToken cancellationToken = default)
    {
        var product = await products.GetByIdWithDetailsAsync(id, cancellationToken);

        if (product is null)
        {
            return NotFound(id);
        }

        var validation = await ValidateReferencesAsync(dto.CategoryId, dto.BrandId, cancellationToken);

        if (validation is not null)
        {
            return validation;
        }

        if (!string.IsNullOrWhiteSpace(dto.Slug))
        {
            if (!TryBuildSlug(dto.Slug, dto.NameEn, out var slug, out var slugFailure))
            {
                return slugFailure!;
            }

            if (slug!.Value != product.Slug.Value)
            {
                if (await products.SlugExistsAsync(slug.Value, id, cancellationToken))
                {
                    return SlugTaken(slug.Value);
                }

                product.Slug = slug;
            }
        }

        if (!string.Equals(product.Code, dto.Code, StringComparison.Ordinal)
            && await products.CodeExistsAsync(dto.Code, id, cancellationToken))
        {
            return GeneralResponse<ProductDetailDto>.Fail(
                CatalogErrors.ProductCodeTaken, $"The product code '{dto.Code}' is already in use.");
        }

        product.Code = dto.Code.Trim();
        product.Name = LocalizedText.Create(dto.NameEn, dto.NameBn);
        product.ShortDescription = Optional(dto.ShortDescriptionEn, dto.ShortDescriptionBn);
        product.Description = Optional(dto.DescriptionEn, dto.DescriptionBn);
        product.CategoryId = dto.CategoryId;
        product.BrandId = dto.BrandId;
        product.ProductType = dto.ProductType;
        product.BasePrice = Money.Taka(dto.BasePrice);
        product.CompareAtPrice = dto.CompareAtPrice is { } compare ? Money.Taka(compare) : null;
        product.LengthCm = dto.LengthCm;
        product.WidthCm = dto.WidthCm;
        product.HeightCm = dto.HeightCm;
        product.WeightKg = dto.WeightKg;
        product.Material = dto.Material;
        product.FinishType = dto.FinishType;
        product.WarrantyMonths = dto.WarrantyMonths;
        product.LeadTimeDays = dto.LeadTimeDays;
        product.AssemblyRequired = dto.AssemblyRequired;
        product.DeliverySurcharge = dto.DeliverySurcharge is { } surcharge ? Money.Taka(surcharge) : null;
        product.IsFeatured = dto.IsFeatured;
        product.SeoTitle = dto.SeoTitle;
        product.SeoDescription = dto.SeoDescription;

        products.Update(product);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return GeneralResponse<ProductDetailDto>.Success(
            CatalogMapper.ToDetail(product), "Product updated.", id);
    }

    public async Task<GeneralResponse<ProductDetailDto>> ChangeStatusAsync(
        long id, ChangeProductStatusDto dto, CancellationToken cancellationToken = default)
    {
        var product = await products.GetByIdWithDetailsAsync(id, cancellationToken);

        if (product is null)
        {
            return NotFound(id);
        }

        product.Status = dto.Status;

        // Stamped once, on first publish, and never rewritten. Archiving and
        // re-activating must not move it: it anchors "new arrivals" ordering
        // and the sitemap, and a product that keeps re-appearing as new because
        // someone toggled it is a ranking problem.
        if (dto.Status == ProductStatus.Active && product.PublishedAt is null)
        {
            product.PublishedAt = clock.UtcNow;
        }

        products.Update(product);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return GeneralResponse<ProductDetailDto>.Success(
            CatalogMapper.ToDetail(product), $"Product is now {dto.Status}.", id);
    }

    public async Task<GeneralResponse> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var product = await products.GetByIdAsync(id, cancellationToken);

        if (product is null)
        {
            return GeneralResponse.Fail(CatalogErrors.ProductNotFound, $"No product with id {id}.");
        }

        products.Delete(product);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return GeneralResponse.Success("Product deleted.", id);
    }

    // -------------------------------------------------------------------------
    // Variants
    // -------------------------------------------------------------------------

    public async Task<GeneralResponse<ProductVariantDto>> AddVariantAsync(
        long productId, CreateProductVariantDto dto, CancellationToken cancellationToken = default)
    {
        var product = await products.GetByIdAsync(productId, cancellationToken);

        if (product is null)
        {
            return GeneralResponse<ProductVariantDto>.Fail(
                CatalogErrors.ProductNotFound, $"No product with id {productId}.");
        }

        if (await variants.SkuExistsAsync(dto.Sku, null, cancellationToken))
        {
            return GeneralResponse<ProductVariantDto>.Fail(
                CatalogErrors.VariantSkuTaken, $"The SKU '{dto.Sku}' is already in use.");
        }

        var variant = new ProductVariant
        {
            ProductId = productId,
            Product = product,
            Sku = dto.Sku.Trim(),
            VariantName = ResolveVariantName(dto, product),
            OptionValues = new Dictionary<string, string>(dto.OptionValues, StringComparer.Ordinal),
            PriceOverride = dto.PriceOverride is { } price ? Money.Taka(price) : null,
            CompareAtPriceOverride = dto.CompareAtPriceOverride is { } compare
                ? Money.Taka(compare)
                : null,
            Barcode = dto.Barcode,
            WeightKg = dto.WeightKg,
            IsActive = dto.IsActive,
            SortOrder = await variants.MaxSortOrderAsync(productId, cancellationToken) + 1
        };

        return await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            if (dto.IsDefault)
            {
                await ClearExistingDefaultAsync(productId, ct);
                variant.IsDefault = true;
            }

            await variants.InsertAsync(variant, ct);
            await unitOfWork.SaveChangesAsync(ct);

            return GeneralResponse<ProductVariantDto>.Success(
                CatalogMapper.ToDto(variant), "Variant added.", variant.Id);
        }, cancellationToken);
    }

    public async Task<GeneralResponse<ProductVariantDto>> UpdateVariantAsync(
        long variantId, UpdateProductVariantDto dto, CancellationToken cancellationToken = default)
    {
        var variant = await variants.GetByIdAsync(variantId, cancellationToken);

        if (variant is null)
        {
            return GeneralResponse<ProductVariantDto>.Fail(
                CatalogErrors.VariantNotFound, $"No variant with id {variantId}.");
        }

        if (!string.Equals(variant.Sku, dto.Sku, StringComparison.Ordinal)
            && await variants.SkuExistsAsync(dto.Sku, variantId, cancellationToken))
        {
            return GeneralResponse<ProductVariantDto>.Fail(
                CatalogErrors.VariantSkuTaken, $"The SKU '{dto.Sku}' is already in use.");
        }

        var product = await products.GetByIdAsync(variant.ProductId, cancellationToken);

        variant.Sku = dto.Sku.Trim();
        variant.VariantName = ResolveVariantName(dto, product);
        variant.OptionValues = new Dictionary<string, string>(dto.OptionValues, StringComparer.Ordinal);
        variant.PriceOverride = dto.PriceOverride is { } price ? Money.Taka(price) : null;
        variant.CompareAtPriceOverride = dto.CompareAtPriceOverride is { } compare
            ? Money.Taka(compare)
            : null;
        variant.Barcode = dto.Barcode;
        variant.WeightKg = dto.WeightKg;
        variant.IsActive = dto.IsActive;

        return await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            if (dto.IsDefault && !variant.IsDefault)
            {
                await ClearExistingDefaultAsync(variant.ProductId, ct);
                variant.IsDefault = true;
            }

            variants.Update(variant);
            await unitOfWork.SaveChangesAsync(ct);

            // Reload so EffectivePrice can fall back to the product's price
            // when this variant has no override.
            variant.Product = product!;

            return GeneralResponse<ProductVariantDto>.Success(
                CatalogMapper.ToDto(variant), "Variant updated.", variantId);
        }, cancellationToken);
    }

    public async Task<GeneralResponse> DeleteVariantAsync(
        long variantId, CancellationToken cancellationToken = default)
    {
        var variant = await variants.GetByIdAsync(variantId, cancellationToken);

        if (variant is null)
        {
            return GeneralResponse.Fail(CatalogErrors.VariantNotFound, $"No variant with id {variantId}.");
        }

        var siblings = await variants.GetByProductAsync(variant.ProductId, cancellationToken);

        // The invariant that gives the catalog its shape. A product with no
        // variants has no price and no stock — it cannot be added to a cart,
        // and nothing on the storefront reports why.
        if (siblings.Count(v => !v.IsDeleted) <= 1)
        {
            return GeneralResponse.Fail(
                CatalogErrors.LastVariant,
                "A product must keep at least one variant. Delete the product instead, or add another variant first.");
        }

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            variants.Delete(variant);

            // Promote a successor rather than leaving the product with no
            // default. Without this the product page has nothing selected and
            // shows no price at all.
            if (variant.IsDefault)
            {
                var successor = siblings
                    .Where(v => v.Id != variantId && !v.IsDeleted)
                    .OrderBy(v => v.SortOrder)
                    .First();

                successor.IsDefault = true;
                variants.Update(successor);
            }

            await unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);

        return GeneralResponse.Success("Variant deleted.", variantId);
    }

    // -------------------------------------------------------------------------

    /// <summary>
    /// Clears whichever variant currently holds the default flag.
    /// </summary>
    /// <remarks>
    /// A filtered unique index enforces one default per product, so setting a
    /// second one without clearing the first fails as a constraint violation —
    /// a 500 with a Postgres error string rather than anything an admin can act
    /// on. Clearing first turns it into an ordinary update.
    /// </remarks>
    private async Task ClearExistingDefaultAsync(long productId, CancellationToken cancellationToken)
    {
        var current = await variants.GetDefaultAsync(productId, cancellationToken);

        if (current is not null)
        {
            current.IsDefault = false;
            variants.Update(current);

            // Saved before the new default is written, so the two updates never
            // coexist inside one statement batch and trip the index.
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    private static List<ProductVariant> BuildVariants(CreateProductDto dto, Product product)
    {
        if (dto.Variants.Count == 0)
        {
            // A product with no options still needs a variant, because the
            // variant is what carries the SKU and the stock. Deriving it from
            // the product keeps the admin UI from having to invent one.
            return
            [
                new ProductVariant
                {
                    Product = product,
                    Sku = product.Code,
                    VariantName = "Standard",
                    IsDefault = true,
                    IsActive = true,
                    SortOrder = 0
                }
            ];
        }

        var built = dto.Variants.Select((v, index) => new ProductVariant
        {
            Product = product,
            Sku = v.Sku.Trim(),
            VariantName = ResolveVariantName(v, product),
            OptionValues = new Dictionary<string, string>(v.OptionValues, StringComparer.Ordinal),
            PriceOverride = v.PriceOverride is { } price ? Money.Taka(price) : null,
            CompareAtPriceOverride = v.CompareAtPriceOverride is { } compare ? Money.Taka(compare) : null,
            Barcode = v.Barcode,
            WeightKg = v.WeightKg,
            IsActive = v.IsActive,
            IsDefault = v.IsDefault,
            SortOrder = index
        }).ToList();

        // Exactly one default, always. If the caller flagged none — or flagged
        // several — the first wins rather than the request failing: it is a
        // presentation detail, and rejecting a whole product over it would be
        // disproportionate.
        var defaults = built.Where(v => v.IsDefault).ToList();

        if (defaults.Count != 1)
        {
            foreach (var variant in built)
            {
                variant.IsDefault = false;
            }

            built[0].IsDefault = true;
        }

        return built;
    }

    /// <summary>Builds "Segun · 6ft" from the options when no name was supplied.</summary>
    private static string ResolveVariantName(CreateProductVariantDto dto, Product? product)
    {
        if (!string.IsNullOrWhiteSpace(dto.VariantName))
        {
            return dto.VariantName.Trim();
        }

        return dto.OptionValues.Count > 0
            ? string.Join(" · ", dto.OptionValues.Values)
            : product?.Name.En ?? "Standard";
    }

    private async Task<GeneralResponse<ProductDetailDto>?> ValidateReferencesAsync(
        long categoryId, long? brandId, CancellationToken cancellationToken)
    {
        if (await categories.GetByIdAsync(categoryId, cancellationToken) is null)
        {
            return GeneralResponse<ProductDetailDto>.Fail(
                CatalogErrors.CategoryNotFound, $"No category with id {categoryId}.");
        }

        if (brandId is { } id && await brands.GetByIdAsync(id, cancellationToken) is null)
        {
            return GeneralResponse<ProductDetailDto>.Fail(
                CatalogErrors.BrandNotFound, $"No brand with id {id}.");
        }

        return null;
    }

    private static LocalizedText? Optional(string? en, string? bn) =>
        string.IsNullOrWhiteSpace(en) ? null : LocalizedText.Create(en, bn);

    private static bool TryBuildSlug(
        string? supplied, string fallback, out Slug? slug, out GeneralResponse<ProductDetailDto>? failure)
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
            failure = GeneralResponse<ProductDetailDto>.Fail(
                CatalogErrors.SlugNotDerivable,
                "The name does not contain any characters usable in a URL. Supply a slug explicitly.");

            return false;
        }
    }

    private static GeneralResponse<ProductDetailDto> NotFound(long id) =>
        GeneralResponse<ProductDetailDto>.Fail(CatalogErrors.ProductNotFound, $"No product with id {id}.");

    private static GeneralResponse<ProductDetailDto> SlugTaken(string slug) =>
        GeneralResponse<ProductDetailDto>.Fail(
            CatalogErrors.ProductSlugTaken, $"The slug '{slug}' is already in use.");
}
