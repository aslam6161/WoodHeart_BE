using WoodHeart.Domain.Enums.Catalog;

namespace WoodHeart.Repository.Queries;

/// <summary>
/// Filters, sorting and paging for a product listing.
/// </summary>
/// <remarks>
/// <para>
/// Lives in the Repository project rather than beside the service DTOs because
/// the repository is what consumes it, and the dependency rule runs
/// Domain → Repository → Service. A query specification the repository cannot
/// see is not a query specification.
/// </para>
/// <para>
/// Inherits the page-size cap from <see cref="PaginationParams"/>. The admin
/// grid and the storefront both bind straight to this, so the cap protects both.
/// </para>
/// </remarks>
public class ProductQuery : PaginationParams
{
    public long? CategoryId { get; set; }

    /// <summary>
    /// Include products in descendant categories, not just the exact one.
    /// </summary>
    /// <remarks>
    /// What a customer expects clicking "Living Room" — everything in the room,
    /// not only the handful filed directly against the parent. Resolved through
    /// the materialized path, so it stays one indexed prefix scan.
    /// </remarks>
    public bool IncludeDescendantCategories { get; set; }

    public long? BrandId { get; set; }

    public ProductStatus? Status { get; set; }

    public ProductType? ProductType { get; set; }

    public bool? IsFeatured { get; set; }

    /// <summary>
    /// Substring match across the product code and the stored name JSON.
    /// </summary>
    /// <remarks>
    /// Searching the raw JSON matches English and Bangla in one predicate,
    /// which is what an admin typing "সেগুন" into a filter box expects.
    /// It is a substring scan, adequate for an admin grid over a few thousand
    /// products and deliberately not what the storefront will use — that gets
    /// <c>tsvector</c> plus <c>pg_trgm</c> per PLAN.md §11, which is its own
    /// piece of work.
    /// </remarks>
    public string? Search { get; set; }

    public decimal? MinPrice { get; set; }

    public decimal? MaxPrice { get; set; }

    public ProductSort SortBy { get; set; } = ProductSort.Newest;
}

/// <summary>
/// The sorts a product listing supports.
/// </summary>
/// <remarks>
/// An enum rather than a free-text <c>sortBy</c> string, so an unknown value is
/// a binding failure with a clear message instead of a silently ignored
/// parameter — and so no caller can inject an ordering expression.
///
/// <para>
/// <b>There is deliberately no sort by name.</b> The name is a jsonb column, so
/// ordering by it means either ordering by the raw JSON text — which happens to
/// work only because <c>en</c> is serialised first, and would break the day the
/// shape changes — or a Postgres-specific extraction this layer would then have
/// to carry. It arrives with the search work, which needs a denormalised
/// searchable column anyway.
/// </para>
/// </remarks>
public enum ProductSort
{
    /// <summary>Most recently created first. The admin grid default.</summary>
    Newest = 0,

    Oldest = 1,

    PriceLowToHigh = 2,

    PriceHighToLow = 3,

    /// <summary>Most recently published first. Storefront "new arrivals".</summary>
    RecentlyPublished = 4,

    /// <summary>Product code, ascending. Stable and predictable for stocktaking.</summary>
    Code = 5
}
