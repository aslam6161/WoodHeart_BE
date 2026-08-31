using System.ComponentModel.DataAnnotations;

namespace WoodHeart.Service.DTOs.Catalog;

/// <summary>
/// A category as the admin UI and the storefront read it.
/// </summary>
/// <remarks>
/// Both languages travel on the wire rather than a single resolved string. The
/// admin grid needs to show and edit both, and the storefront picks per request
/// — resolving server-side would mean the response could not be cached across
/// languages.
/// </remarks>
public class CategoryDto
{
    public long Id { get; set; }

    public string NameEn { get; set; } = string.Empty;

    public string? NameBn { get; set; }

    public string Slug { get; set; } = string.Empty;

    public string? DescriptionEn { get; set; }

    public string? DescriptionBn { get; set; }

    public long? ParentId { get; set; }

    public int Depth { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; }

    public bool IsFeatured { get; set; }

    public string? ImagePath { get; set; }

    public string? SeoTitle { get; set; }

    public string? SeoDescription { get; set; }

    /// <summary>Live products directly in this category. Excludes descendants.</summary>
    public int ProductCount { get; set; }
}

/// <summary>A category with its children attached, for rendering the tree.</summary>
public class CategoryTreeDto : CategoryDto
{
    public List<CategoryTreeDto> Children { get; set; } = [];
}

public class CreateCategoryDto
{
    [Required(ErrorMessage = "An English name is required.")]
    [StringLength(200, MinimumLength = 1)]
    public string NameEn { get; set; } = string.Empty;

    [StringLength(200)]
    public string? NameBn { get; set; }

    /// <summary>
    /// Leave empty to derive from the name. Supplied explicitly when the
    /// generated one is unhelpful — a Bangla-only name produces a Bangla slug,
    /// which is valid but not always what you want in a URL.
    /// </summary>
    [StringLength(160)]
    public string? Slug { get; set; }

    [StringLength(2000)]
    public string? DescriptionEn { get; set; }

    [StringLength(2000)]
    public string? DescriptionBn { get; set; }

    public long? ParentId { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsFeatured { get; set; }

    [StringLength(512)]
    public string? ImagePath { get; set; }

    [StringLength(200)]
    public string? SeoTitle { get; set; }

    [StringLength(400)]
    public string? SeoDescription { get; set; }
}

/// <summary>
/// An edit. Deliberately does not carry <c>ParentId</c>.
/// </summary>
/// <remarks>
/// Moving a category is a different operation from renaming one: it rewrites
/// the materialized path of every descendant and has to be checked for cycles.
/// Folding it into a general update means every edit pays that cost and every
/// caller can trigger it by accident. Moves go through <c>MoveCategoryDto</c>.
/// </remarks>
public class UpdateCategoryDto
{
    [Required(ErrorMessage = "An English name is required.")]
    [StringLength(200, MinimumLength = 1)]
    public string NameEn { get; set; } = string.Empty;

    [StringLength(200)]
    public string? NameBn { get; set; }

    /// <summary>
    /// Optional. Omitting it keeps the existing slug, which is the safe default
    /// — a published slug is part of the site's SEO and every inbound link.
    /// </summary>
    [StringLength(160)]
    public string? Slug { get; set; }

    [StringLength(2000)]
    public string? DescriptionEn { get; set; }

    [StringLength(2000)]
    public string? DescriptionBn { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsFeatured { get; set; }

    [StringLength(512)]
    public string? ImagePath { get; set; }

    [StringLength(200)]
    public string? SeoTitle { get; set; }

    [StringLength(400)]
    public string? SeoDescription { get; set; }
}

/// <summary>Re-parents a category, and optionally repositions it among its new siblings.</summary>
public class MoveCategoryDto
{
    /// <summary>Null moves it to the root.</summary>
    public long? NewParentId { get; set; }

    /// <summary>Null appends it after the existing children.</summary>
    public int? SortOrder { get; set; }
}
