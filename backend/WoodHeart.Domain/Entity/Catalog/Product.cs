using WoodHeart.Domain.Enums.Catalog;
using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Domain.Entity.Catalog;

/// <summary>
/// A sellable item in the catalog, and the aggregate root of the catalog module.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every product has at least one variant, always.</b> A product on its own
/// is a marketing page: it carries the name, description, images and reviews.
/// The thing a customer actually buys, that has a price and a stock count, is a
/// <see cref="ProductVariant"/>. A "simple" product with no options is modelled
/// as one product with one default variant, and nothing downstream needs a
/// special case for it.
/// </para>
/// <para>
/// This is the single most important shape decision in the catalog. PLAN.md §6.1
/// puts it plainly: a bed in Segun and Mehogoni at two sizes is four SKUs with
/// four prices and four stock counts. Model that as four products and you split
/// the reviews four ways, compete with yourself in Google, and cannot show a
/// size picker. Retrofitting variants onto a flat catalog later is one of the
/// most expensive refactors in commerce software.
/// </para>
/// <para>
/// <b><see cref="BasePrice"/> is a default, not the price.</b> The price a
/// customer pays comes from the variant, which may override it. Cart, checkout
/// and order lines read the variant — never this field.
/// </para>
/// </remarks>
public class Product : SoftDeletableEntity
{
    /// <summary>
    /// Human-facing product code, unique across the catalog. Distinct from a
    /// variant SKU, which is what warehouse staff actually pick.
    /// </summary>
    public string Code { get; set; } = null!;

    public LocalizedText Name { get; set; } = null!;

    /// <summary>Stable once published. Renaming a product does not move its URL.</summary>
    public Slug Slug { get; set; } = null!;

    /// <summary>One or two lines for cards and search results.</summary>
    public LocalizedText? ShortDescription { get; set; }

    /// <summary>Full copy for the detail page. Sanitised HTML.</summary>
    public LocalizedText? Description { get; set; }

    public long CategoryId { get; set; }

    public Category Category { get; set; } = null!;

    public long? BrandId { get; set; }

    public Brand? Brand { get; set; }

    public ProductType ProductType { get; set; } = ProductType.Stocked;

    public ProductStatus Status { get; set; } = ProductStatus.Draft;

    /// <summary>
    /// Set the first time the product goes Active, and never cleared.
    /// Archiving and re-activating must not rewrite it — it anchors "new
    /// arrivals" sorting and the sitemap.
    /// </summary>
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>
    /// The default price, in taka. A variant with no <c>PriceOverride</c>
    /// inherits it.
    /// </summary>
    public Money BasePrice { get; set; } = null!;

    /// <summary>
    /// The "was" price, shown struck through. Must exceed
    /// <see cref="BasePrice"/> to mean anything; enforced in the service, not
    /// here, because it is a merchandising rule rather than an invariant of the
    /// type.
    /// </summary>
    public Money? CompareAtPrice { get; set; }

    // --- Physical characteristics -------------------------------------------
    // Nullable throughout: a Service product has no dimensions, and a made-to-
    // order wardrobe may not have final ones until it is specified.

    public decimal? LengthCm { get; set; }

    public decimal? WidthCm { get; set; }

    public decimal? HeightCm { get; set; }

    public decimal? WeightKg { get; set; }

    /// <summary>Segun, Mehogoni, engineered wood, steel, rattan.</summary>
    public string? Material { get; set; }

    /// <summary>Matte, gloss, natural, walnut stain.</summary>
    public string? FinishType { get; set; }

    public int? WarrantyMonths { get; set; }

    // --- Fulfilment ----------------------------------------------------------

    /// <summary>
    /// Working days to build, for <see cref="ProductType.MadeToOrder"/>.
    /// Snapshotted onto the order line at placement, because quoting a customer
    /// a lead time and then changing the product must not rewrite their promise.
    /// </summary>
    public int? LeadTimeDays { get; set; }

    public bool AssemblyRequired { get; set; }

    /// <summary>
    /// Extra delivery cost for something bulky, on top of the zone rate. A
    /// three-seater sofa is not a table lamp and the flat zone charge does not
    /// cover it.
    /// </summary>
    public Money? DeliverySurcharge { get; set; }

    // --- Merchandising and SEO ----------------------------------------------

    public bool IsFeatured { get; set; }

    public string? SeoTitle { get; set; }

    public string? SeoDescription { get; set; }

    /// <summary>Relative storage key of the social share image.</summary>
    public string? OgImagePath { get; set; }

    /// <summary>
    /// Denormalised counters, maintained when a review is approved. Kept here
    /// so a listing page can sort by rating without joining and aggregating
    /// reviews for every row.
    /// </summary>
    public decimal? AverageRating { get; set; }

    public int ReviewCount { get; set; }

    /// <summary>
    /// Denormalised lower-case haystack: code, both names, material and finish.
    /// Maintained by <c>DataContext</c> on every save.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It exists because the searchable fields are value objects stored as
    /// jsonb, and EF cannot reach inside a value-converted property to build a
    /// predicate — <c>EF.Property&lt;string&gt;(p, "Name")</c> compiles and then
    /// throws <c>InvalidCastException</c> at runtime, because the model type is
    /// <c>LocalizedText</c>, not <c>string</c>.
    /// </para>
    /// <para>
    /// One plain column matches English and Bangla in a single predicate, which
    /// is what an admin typing সেগুন into a filter box expects. It is a
    /// stepping stone, not the destination: PLAN.md §11 commits to
    /// <c>tsvector</c> plus <c>pg_trgm</c> for the storefront, and this column
    /// is what that will be built from.
    /// </para>
    /// </remarks>
    public string SearchText { get; set; } = string.Empty;

    public ICollection<ProductVariant> Variants { get; set; } = [];

    public ICollection<ProductMedia> Media { get; set; } = [];

    public ICollection<Collection> Collections { get; set; } = [];

    /// <summary>Visible to the public API: Active, published, not deleted.</summary>
    public bool IsPubliclyVisible => Status == ProductStatus.Active && !IsDeleted;

    /// <summary>
    /// The variant a product page selects when nothing is chosen — the one
    /// flagged default, else the first by sort order.
    /// </summary>
    public ProductVariant? DefaultVariant =>
        Variants.FirstOrDefault(v => v.IsDefault && !v.IsDeleted)
        ?? Variants.Where(v => !v.IsDeleted).OrderBy(v => v.SortOrder).FirstOrDefault();

    /// <summary>Whether this product participates in stock at all.</summary>
    public bool TracksStock => ProductType == ProductType.Stocked;
}
