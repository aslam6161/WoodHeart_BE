using System.ComponentModel.DataAnnotations;
using WoodHeart.Domain.Enums.Catalog;

namespace WoodHeart.Service.DTOs.Catalog;

/// <summary>One row of a product listing. Deliberately not the full product.</summary>
/// <remarks>
/// A listing page returns dozens of these. Shipping the full description — two
/// languages of HTML — on every row is the difference between a fast grid and a
/// slow one, and none of it is rendered.
/// </remarks>
public class ProductListItemDto
{
    public long Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string? NameBn { get; set; }

    public string Slug { get; set; } = string.Empty;

    public long CategoryId { get; set; }

    public string CategoryNameEn { get; set; } = string.Empty;

    public long? BrandId { get; set; }

    public string? BrandNameEn { get; set; }

    public ProductType ProductType { get; set; }

    public ProductStatus Status { get; set; }

    public decimal BasePrice { get; set; }

    public decimal? CompareAtPrice { get; set; }

    /// <summary>Always BDT in v1. Sent so the client never hard-codes a symbol.</summary>
    public string Currency { get; set; } = "BDT";

    public bool IsFeatured { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public int VariantCount { get; set; }

    /// <summary>Storage key of the primary image, or null. Not a URL.</summary>
    public string? PrimaryImagePath { get; set; }
}

public class ProductDetailDto : ProductListItemDto
{
    public string? ShortDescriptionEn { get; set; }

    public string? ShortDescriptionBn { get; set; }

    public string? DescriptionEn { get; set; }

    public string? DescriptionBn { get; set; }

    public decimal? LengthCm { get; set; }

    public decimal? WidthCm { get; set; }

    public decimal? HeightCm { get; set; }

    public decimal? WeightKg { get; set; }

    public string? Material { get; set; }

    public string? FinishType { get; set; }

    public int? WarrantyMonths { get; set; }

    public int? LeadTimeDays { get; set; }

    public bool AssemblyRequired { get; set; }

    /// <summary>What one costs to deliver inside Dhaka. Null uses the store default.</summary>
    public decimal? DeliveryChargeInsideDhaka { get; set; }

    /// <summary>The same, for everywhere else.</summary>
    public decimal? DeliveryChargeOutsideDhaka { get; set; }

    public string? SeoTitle { get; set; }

    public string? SeoDescription { get; set; }

    public string? OgImagePath { get; set; }

    public decimal? AverageRating { get; set; }

    public int ReviewCount { get; set; }

    public List<ProductVariantDto> Variants { get; set; } = [];

    public List<ProductMediaDto> Media { get; set; } = [];
}

public class ProductVariantDto
{
    public long Id { get; set; }

    public long ProductId { get; set; }

    public string Sku { get; set; } = string.Empty;

    public string VariantName { get; set; } = string.Empty;

    public Dictionary<string, string> OptionValues { get; set; } = [];

    /// <summary>
    /// What the customer pays: the variant's override, or the product's base
    /// price. Resolved server-side so no client has to know the fallback rule.
    /// </summary>
    public decimal EffectivePrice { get; set; }

    public decimal? EffectiveCompareAtPrice { get; set; }

    public bool IsOnOffer { get; set; }

    public string? Barcode { get; set; }

    public decimal? WeightKg { get; set; }

    public bool IsDefault { get; set; }

    public bool IsActive { get; set; }

    public int SortOrder { get; set; }
}

public class ProductMediaDto
{
    public long Id { get; set; }

    public long? VariantId { get; set; }

    public MediaType MediaType { get; set; }

    public string StoragePath { get; set; } = string.Empty;

    public string? AltText { get; set; }

    public string? Caption { get; set; }

    public bool IsPrimary { get; set; }

    public int SortOrder { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public string? ExternalUrl { get; set; }
}

// -----------------------------------------------------------------------------
// Writes
// -----------------------------------------------------------------------------

public class CreateProductDto
{
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "An English name is required.")]
    [StringLength(300, MinimumLength = 1)]
    public string NameEn { get; set; } = string.Empty;

    [StringLength(300)]
    public string? NameBn { get; set; }

    /// <summary>Leave empty to derive from the name.</summary>
    [StringLength(160)]
    public string? Slug { get; set; }

    [Required]
    public long CategoryId { get; set; }

    public long? BrandId { get; set; }

    public ProductType ProductType { get; set; } = ProductType.Stocked;

    [Range(0, 99_999_999)]
    public decimal BasePrice { get; set; }

    [Range(0, 99_999_999)]
    public decimal? CompareAtPrice { get; set; }

    [StringLength(500)]
    public string? ShortDescriptionEn { get; set; }

    [StringLength(500)]
    public string? ShortDescriptionBn { get; set; }

    public string? DescriptionEn { get; set; }

    public string? DescriptionBn { get; set; }

    [Range(0, 10_000)]
    public decimal? LengthCm { get; set; }

    [Range(0, 10_000)]
    public decimal? WidthCm { get; set; }

    [Range(0, 10_000)]
    public decimal? HeightCm { get; set; }

    [Range(0, 10_000)]
    public decimal? WeightKg { get; set; }

    [StringLength(128)]
    public string? Material { get; set; }

    [StringLength(128)]
    public string? FinishType { get; set; }

    [Range(0, 600)]
    public int? WarrantyMonths { get; set; }

    [Range(0, 365)]
    public int? LeadTimeDays { get; set; }

    public bool AssemblyRequired { get; set; }

    /// <summary>
    /// What one of these costs to deliver inside Dhaka.
    /// </summary>
    /// <remarks>
    /// Leave blank to use the store's default rate. <b>Blank means "the
    /// ordinary charge", not "free"</b> — the most common mistake here would
    /// otherwise be shipping a wardrobe for nothing.
    /// </remarks>
    [Range(0, 99_999_999)]
    public decimal? DeliveryChargeInsideDhaka { get; set; }

    /// <summary>What one costs to deliver outside Dhaka. Blank uses the store default.</summary>
    [Range(0, 99_999_999)]
    public decimal? DeliveryChargeOutsideDhaka { get; set; }

    public bool IsFeatured { get; set; }

    [StringLength(200)]
    public string? SeoTitle { get; set; }

    [StringLength(400)]
    public string? SeoDescription { get; set; }

    /// <summary>
    /// The configurations this product is sold in.
    /// </summary>
    /// <remarks>
    /// Optional, and when it is empty the service creates one default variant
    /// carrying the product's own price. That is not a convenience — it is how
    /// the "every product has at least one variant" invariant is kept without
    /// making the admin UI invent a variant for a product that has no options.
    /// </remarks>
    public List<CreateProductVariantDto> Variants { get; set; } = [];
}

/// <summary>
/// An edit. Does not carry variants — those have their own endpoints.
/// </summary>
/// <remarks>
/// Accepting a variant list here would mean deciding what an omitted variant
/// means. Every plausible answer is dangerous: silently deleting one destroys
/// its stock ledger and its order history links, and silently ignoring the
/// omission makes deletion impossible through the obvious route.
/// </remarks>
public class UpdateProductDto
{
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "An English name is required.")]
    [StringLength(300, MinimumLength = 1)]
    public string NameEn { get; set; } = string.Empty;

    [StringLength(300)]
    public string? NameBn { get; set; }

    /// <summary>Optional. Omitting it keeps the published slug, which is the safe default.</summary>
    [StringLength(160)]
    public string? Slug { get; set; }

    [Required]
    public long CategoryId { get; set; }

    public long? BrandId { get; set; }

    public ProductType ProductType { get; set; } = ProductType.Stocked;

    [Range(0, 99_999_999)]
    public decimal BasePrice { get; set; }

    [Range(0, 99_999_999)]
    public decimal? CompareAtPrice { get; set; }

    [StringLength(500)]
    public string? ShortDescriptionEn { get; set; }

    [StringLength(500)]
    public string? ShortDescriptionBn { get; set; }

    public string? DescriptionEn { get; set; }

    public string? DescriptionBn { get; set; }

    [Range(0, 10_000)]
    public decimal? LengthCm { get; set; }

    [Range(0, 10_000)]
    public decimal? WidthCm { get; set; }

    [Range(0, 10_000)]
    public decimal? HeightCm { get; set; }

    [Range(0, 10_000)]
    public decimal? WeightKg { get; set; }

    [StringLength(128)]
    public string? Material { get; set; }

    [StringLength(128)]
    public string? FinishType { get; set; }

    [Range(0, 600)]
    public int? WarrantyMonths { get; set; }

    [Range(0, 365)]
    public int? LeadTimeDays { get; set; }

    public bool AssemblyRequired { get; set; }

    /// <summary>
    /// What one of these costs to deliver inside Dhaka.
    /// </summary>
    /// <remarks>
    /// Leave blank to use the store's default rate. <b>Blank means "the
    /// ordinary charge", not "free"</b> — the most common mistake here would
    /// otherwise be shipping a wardrobe for nothing.
    /// </remarks>
    [Range(0, 99_999_999)]
    public decimal? DeliveryChargeInsideDhaka { get; set; }

    /// <summary>What one costs to deliver outside Dhaka. Blank uses the store default.</summary>
    [Range(0, 99_999_999)]
    public decimal? DeliveryChargeOutsideDhaka { get; set; }

    public bool IsFeatured { get; set; }

    [StringLength(200)]
    public string? SeoTitle { get; set; }

    [StringLength(400)]
    public string? SeoDescription { get; set; }
}

public class CreateProductVariantDto
{
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string Sku { get; set; } = string.Empty;

    /// <summary>
    /// Leave empty to build it from the options — "Segun · 6ft". Supplied when
    /// the generated form is not what should appear on an invoice.
    /// </summary>
    [StringLength(256)]
    public string? VariantName { get; set; }

    /// <summary>The option combination, e.g. <c>{"Wood":"Segun","Size":"6ft"}</c>.</summary>
    public Dictionary<string, string> OptionValues { get; set; } = [];

    /// <summary>Null inherits the product's base price.</summary>
    [Range(0, 99_999_999)]
    public decimal? PriceOverride { get; set; }

    [Range(0, 99_999_999)]
    public decimal? CompareAtPriceOverride { get; set; }

    [StringLength(64)]
    public string? Barcode { get; set; }

    /// <summary>Overrides the product's weight. Null inherits it.</summary>
    [Range(0, 10_000)]
    public decimal? WeightKg { get; set; }

    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;
}

public class UpdateProductVariantDto : CreateProductVariantDto;

/// <summary>Publishes or withdraws a product.</summary>
public class ChangeProductStatusDto
{
    [Required]
    public ProductStatus Status { get; set; }
}
