using WoodHeart.Domain.Enums.Catalog;

namespace WoodHeart.Service.DTOs.Catalog;

/// <summary>
/// A product card on the storefront.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately a separate type from <see cref="ProductListItemDto"/>, not a
/// subclass or a reuse.</b> The admin DTO carries <c>Status</c>, the internal
/// product code and audit-adjacent fields. Every one of those is harmless
/// today, and the point is what happens next year: someone adds
/// <c>SupplierCostPrice</c> to the admin DTO for a margin report, and if the
/// storefront shared the type it would immediately start publishing the cost of
/// every product to anyone with the developer console open.
/// </para>
/// <para>
/// Two types is a small, permanent cost. One type is a leak waiting for the
/// right field to be added.
/// </para>
/// </remarks>
public class StorefrontProductDto
{
    public long Id { get; set; }

    public string Slug { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string? NameBn { get; set; }

    public string? ShortDescriptionEn { get; set; }

    public string? ShortDescriptionBn { get; set; }

    public string CategorySlug { get; set; } = string.Empty;

    public string CategoryNameEn { get; set; } = string.Empty;

    public string? BrandNameEn { get; set; }

    public ProductType ProductType { get; set; }

    /// <summary>The lowest effective price across the product's active variants.</summary>
    /// <remarks>
    /// A card shows "from ৳45,000". Showing the base price instead would be
    /// wrong for any product whose cheapest variant overrides it downward.
    /// </remarks>
    public decimal FromPrice { get; set; }

    public decimal? CompareAtPrice { get; set; }

    public string Currency { get; set; } = "BDT";

    public bool IsOnOffer { get; set; }

    /// <summary>Whole-percent discount, for a badge. Null when there is no offer.</summary>
    public int? DiscountPercent { get; set; }

    public bool IsFeatured { get; set; }

    /// <summary>Working days to build. Null for stocked products.</summary>
    public int? LeadTimeDays { get; set; }

    public decimal? AverageRating { get; set; }

    public int ReviewCount { get; set; }

    public string? PrimaryImagePath { get; set; }

    public string? PrimaryImageAlt { get; set; }

    public int VariantCount { get; set; }
}

/// <summary>A product page.</summary>
public class StorefrontProductDetailDto : StorefrontProductDto
{
    public string? DescriptionEn { get; set; }

    public string? DescriptionBn { get; set; }

    public decimal? LengthCm { get; set; }

    public decimal? WidthCm { get; set; }

    public decimal? HeightCm { get; set; }

    public decimal? WeightKg { get; set; }

    public string? Material { get; set; }

    public string? FinishType { get; set; }

    public int? WarrantyMonths { get; set; }

    public bool AssemblyRequired { get; set; }

    /// <summary>
    /// Delivery for one of these inside Dhaka, so the product page can say what
    /// carriage costs before the customer reaches checkout.
    /// </summary>
    public decimal? DeliveryChargeInsideDhaka { get; set; }

    public decimal? DeliveryChargeOutsideDhaka { get; set; }

    /// <summary>The breadcrumb trail, root first. Drives the on-page trail and the JSON-LD.</summary>
    public List<StorefrontBreadcrumbDto> Breadcrumbs { get; set; } = [];

    public StorefrontSeoDto Seo { get; set; } = new();

    public List<StorefrontVariantDto> Variants { get; set; } = [];

    public List<StorefrontMediaDto> Media { get; set; } = [];
}

public class StorefrontVariantDto
{
    public long Id { get; set; }

    public string Sku { get; set; } = string.Empty;

    public string VariantName { get; set; } = string.Empty;

    public Dictionary<string, string> OptionValues { get; set; } = [];

    public decimal Price { get; set; }

    public decimal? CompareAtPrice { get; set; }

    public bool IsOnOffer { get; set; }

    public bool IsDefault { get; set; }
}

public class StorefrontMediaDto
{
    public long Id { get; set; }

    /// <summary>Null means the image belongs to the product rather than one variant.</summary>
    public long? VariantId { get; set; }

    public MediaType MediaType { get; set; }

    public string StoragePath { get; set; } = string.Empty;

    public string? AltText { get; set; }

    public string? Caption { get; set; }

    public bool IsPrimary { get; set; }

    /// <summary>
    /// Intrinsic dimensions, so the client can reserve the space before the
    /// image loads. Omitting them is a Cumulative Layout Shift penalty on
    /// exactly the pages that need to rank.
    /// </summary>
    public int? Width { get; set; }

    public int? Height { get; set; }

    public string? ExternalUrl { get; set; }
}

public class StorefrontBreadcrumbDto
{
    public string NameEn { get; set; } = string.Empty;

    public string? NameBn { get; set; }

    public string Slug { get; set; } = string.Empty;
}

/// <summary>
/// Everything the page needs for its head tags and structured data.
/// </summary>
/// <remarks>
/// Composed server-side, with fallbacks already applied, because the Angular
/// app renders these during SSR and a missing meta description is invisible
/// until someone checks Search Console months later.
/// </remarks>
public class StorefrontSeoDto
{
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Path only, e.g. <c>/products/segun-king-bed</c>. The host is the client's business.</summary>
    public string CanonicalPath { get; set; } = string.Empty;

    public string? OgImagePath { get; set; }
}

/// <summary>A collection landing page.</summary>
public class StorefrontCollectionDto
{
    public long Id { get; set; }

    public string Slug { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string? NameBn { get; set; }

    public string? DescriptionEn { get; set; }

    public string? DescriptionBn { get; set; }

    public string? BannerPath { get; set; }

    public string? ThumbnailPath { get; set; }

    public StorefrontSeoDto Seo { get; set; } = new();
}
