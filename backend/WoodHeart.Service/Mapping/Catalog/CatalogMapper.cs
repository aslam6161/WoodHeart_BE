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
}
