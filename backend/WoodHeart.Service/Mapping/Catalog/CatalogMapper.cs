using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Service.DTOs.Catalog;

namespace WoodHeart.Service.Mapping.Catalog;

/// <summary>
/// Entity to DTO for the catalog.
/// </summary>
/// <remarks>
/// <para>
/// Written by hand rather than generated. Mapperly writes assignments for
/// matching members, and almost nothing here matches: <c>Name</c> is a
/// <c>LocalizedText</c> that becomes two flat strings, <c>Slug</c> is a value
/// object that becomes one. Configuring a generator to do that costs more
/// attribute noise than the assignments it saves, and reads worse.
/// </para>
/// <para>
/// The rule the generator would otherwise enforce — a new property on the
/// entity is a compile error until it is mapped — is carried by the DTO tests
/// instead.
/// </para>
/// </remarks>
public static class CatalogMapper
{
    public static CategoryDto ToDto(Category category) => Fill(new CategoryDto(), category);

    public static CategoryTreeDto ToTreeDto(Category category) => Fill(new CategoryTreeDto(), category);

    private static T Fill<T>(T dto, Category category) where T : CategoryDto
    {
        dto.Id = category.Id;
        dto.NameEn = category.Name.En;
        dto.NameBn = category.Name.Bn;
        dto.Slug = category.Slug.Value;
        dto.DescriptionEn = category.Description?.En;
        dto.DescriptionBn = category.Description?.Bn;
        dto.ParentId = category.ParentId;
        dto.Depth = category.Depth;
        dto.SortOrder = category.SortOrder;
        dto.IsActive = category.IsActive;
        dto.IsFeatured = category.IsFeatured;
        dto.ImagePath = category.ImagePath;
        dto.SeoTitle = category.SeoTitle;
        dto.SeoDescription = category.SeoDescription;

        return dto;
    }

    public static BrandDto ToDto(Brand brand) => new()
    {
        Id = brand.Id,
        NameEn = brand.Name.En,
        NameBn = brand.Name.Bn,
        Slug = brand.Slug.Value,
        DescriptionEn = brand.Description?.En,
        DescriptionBn = brand.Description?.Bn,
        LogoPath = brand.LogoPath,
        IsActive = brand.IsActive,
        SortOrder = brand.SortOrder
    };

    // -------------------------------------------------------------------------
    // Products
    // -------------------------------------------------------------------------

    public static ProductListItemDto ToListItem(Product product) =>
        FillListItem(new ProductListItemDto(), product);

    public static ProductDetailDto ToDetail(Product product)
    {
        var dto = FillListItem(new ProductDetailDto(), product);

        dto.ShortDescriptionEn = product.ShortDescription?.En;
        dto.ShortDescriptionBn = product.ShortDescription?.Bn;
        dto.DescriptionEn = product.Description?.En;
        dto.DescriptionBn = product.Description?.Bn;
        dto.LengthCm = product.LengthCm;
        dto.WidthCm = product.WidthCm;
        dto.HeightCm = product.HeightCm;
        dto.WeightKg = product.WeightKg;
        dto.Material = product.Material;
        dto.FinishType = product.FinishType;
        dto.WarrantyMonths = product.WarrantyMonths;
        dto.LeadTimeDays = product.LeadTimeDays;
        dto.AssemblyRequired = product.AssemblyRequired;
        dto.DeliverySurcharge = product.DeliverySurcharge?.Amount;
        dto.SeoTitle = product.SeoTitle;
        dto.SeoDescription = product.SeoDescription;
        dto.OgImagePath = product.OgImagePath;
        dto.AverageRating = product.AverageRating;
        dto.ReviewCount = product.ReviewCount;

        dto.Variants = [.. product.Variants
            .Where(v => !v.IsDeleted)
            .OrderBy(v => v.SortOrder)
            .Select(ToDto)];

        dto.Media = [.. product.Media
            .Where(m => !m.IsDeleted)
            .OrderBy(m => m.SortOrder)
            .Select(ToDto)];

        return dto;
    }

    private static T FillListItem<T>(T dto, Product product) where T : ProductListItemDto
    {
        dto.Id = product.Id;
        dto.Code = product.Code;
        dto.NameEn = product.Name.En;
        dto.NameBn = product.Name.Bn;
        dto.Slug = product.Slug.Value;
        dto.CategoryId = product.CategoryId;
        dto.CategoryNameEn = product.Category?.Name.En ?? string.Empty;
        dto.BrandId = product.BrandId;
        dto.BrandNameEn = product.Brand?.Name.En;
        dto.ProductType = product.ProductType;
        dto.Status = product.Status;
        dto.BasePrice = product.BasePrice.Amount;
        dto.CompareAtPrice = product.CompareAtPrice?.Amount;
        dto.Currency = product.BasePrice.Currency;
        dto.IsFeatured = product.IsFeatured;
        dto.PublishedAt = product.PublishedAt;
        dto.VariantCount = product.Variants.Count(v => !v.IsDeleted);
        dto.PrimaryImagePath = product.Media
            .FirstOrDefault(m => m.IsPrimary && !m.IsDeleted)?.StoragePath;

        return dto;
    }

    public static ProductVariantDto ToDto(ProductVariant variant) => new()
    {
        Id = variant.Id,
        ProductId = variant.ProductId,
        Sku = variant.Sku,
        VariantName = variant.VariantName,
        OptionValues = new Dictionary<string, string>(variant.OptionValues, StringComparer.Ordinal),
        // Resolved here rather than on the client, so nothing downstream has to
        // know that a null override means "inherit the product's price".
        EffectivePrice = variant.PriceOverride?.Amount ?? variant.Product?.BasePrice.Amount ?? 0m,
        EffectiveCompareAtPrice =
            variant.CompareAtPriceOverride?.Amount ?? variant.Product?.CompareAtPrice?.Amount,
        Barcode = variant.Barcode,
        WeightKg = variant.WeightKg,
        IsDefault = variant.IsDefault,
        IsActive = variant.IsActive,
        SortOrder = variant.SortOrder
    };

    public static ProductMediaDto ToDto(ProductMedia media) => new()
    {
        Id = media.Id,
        VariantId = media.VariantId,
        MediaType = media.MediaType,
        StoragePath = media.StoragePath,
        AltText = media.AltText,
        Caption = media.Caption,
        IsPrimary = media.IsPrimary,
        SortOrder = media.SortOrder,
        Width = media.Width,
        Height = media.Height,
        ExternalUrl = media.ExternalUrl
    };
}
