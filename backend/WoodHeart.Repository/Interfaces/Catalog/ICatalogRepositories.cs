using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Repository.Queries;

namespace WoodHeart.Repository.Interfaces.Catalog;

/// <summary>
/// Category reads and the queries the tree needs.
/// </summary>
public interface ICategoryRepository : IRepository<Category>
{
    Task<Category?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Whether a slug is already taken by a live category other than <paramref name="excludingId"/>.</summary>
    Task<bool> SlugExistsAsync(
        string slug, long? excludingId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// The whole tree in one query, ordered so a caller can build the hierarchy
    /// without recursing into the database.
    /// </summary>
    /// <remarks>
    /// Deliberately loads every category rather than fetching children on
    /// demand. The tree is a few hundred rows at most and is rendered on every
    /// page of the storefront; one query the result of which gets cached beats
    /// a query per expanded node.
    /// </remarks>
    Task<IReadOnlyList<Category>> GetTreeAsync(
        bool includeInactive = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// A category and everything beneath it, found by materialized-path prefix.
    /// </summary>
    /// <remarks>
    /// Used when moving or deleting a subtree, and to answer "products in this
    /// category, including its children" — which is what a customer expects
    /// clicking "Living Room".
    /// </remarks>
    Task<IReadOnlyList<Category>> GetSubtreeAsync(
        string materializedPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// The ancestor chain for a category, root first, including the category
    /// itself. Drives breadcrumbs and their JSON-LD.
    /// </summary>
    /// <remarks>
    /// Reads the ids straight out of the materialized path, so it is one
    /// <c>WHERE id = ANY(...)</c> rather than a walk up the parent pointers —
    /// which would be a query per level, on every product page.
    /// </remarks>
    Task<IReadOnlyList<Category>> GetAncestorsAsync(
        string materializedPath, CancellationToken cancellationToken = default);

    Task<bool> HasChildrenAsync(long categoryId, CancellationToken cancellationToken = default);

    Task<bool> HasProductsAsync(long categoryId, CancellationToken cancellationToken = default);

    /// <summary>The highest sort order among a parent's children, for appending a new one.</summary>
    Task<int> MaxSortOrderAsync(long? parentId, CancellationToken cancellationToken = default);

    /// <summary>Live product counts keyed by category id.</summary>
    /// <remarks>
    /// One grouped query for the whole tree. Counting per node would be a query
    /// per category on a page that renders every category — the textbook N+1.
    /// </remarks>
    Task<IReadOnlyDictionary<long, int>> GetProductCountsAsync(
        CancellationToken cancellationToken = default);
}

public interface IBrandRepository : IRepository<Brand>
{
    Task<IReadOnlyList<Brand>> GetAllAsync(
        bool includeInactive, CancellationToken cancellationToken = default);

    /// <summary>Product counts keyed by brand id, in one grouped query.</summary>
    Task<IReadOnlyDictionary<long, int>> GetProductCountsAsync(
        CancellationToken cancellationToken = default);

    Task<Brand?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    Task<bool> SlugExistsAsync(
        string slug, long? excludingId = null, CancellationToken cancellationToken = default);

    Task<bool> HasProductsAsync(long brandId, CancellationToken cancellationToken = default);
}

public interface IProductRepository : IRepository<Product>
{
    /// <summary>
    /// A filtered, sorted page of products with the data a list row needs.
    /// </summary>
    /// <remarks>
    /// Returns entities rather than DTOs so the Service layer keeps ownership
    /// of the wire shape — but the filtering, sorting and paging all happen in
    /// SQL. Loading the catalog and filtering in memory is the mistake this
    /// signature exists to prevent.
    /// </remarks>
    Task<PagedList<Product>> SearchAsync(
        ProductQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// A product with everything the detail page needs — category, brand,
    /// variants and media — in one round trip.
    /// </summary>
    Task<Product?> GetBySlugWithDetailsAsync(
        string slug, CancellationToken cancellationToken = default);

    Task<Product?> GetByIdWithDetailsAsync(long id, CancellationToken cancellationToken = default);

    Task<bool> SlugExistsAsync(
        string slug, long? excludingId = null, CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(
        string code, long? excludingId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Other published products in the same category, cheapest first, excluding
    /// the one being viewed.
    /// </summary>
    Task<IReadOnlyList<Product>> GetRelatedAsync(
        long productId, long categoryId, int count, CancellationToken cancellationToken = default);
}

public interface IProductVariantRepository : IRepository<ProductVariant>
{
    /// <summary>The variant currently flagged default for a product, if any.</summary>
    Task<ProductVariant?> GetDefaultAsync(
        long productId, CancellationToken cancellationToken = default);

    Task<int> MaxSortOrderAsync(long productId, CancellationToken cancellationToken = default);

    Task<bool> SkuExistsAsync(
        string sku, long? excludingId = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductVariant>> GetByProductAsync(
        long productId, CancellationToken cancellationToken = default);
}

/// <summary>Images, video and documents attached to a product.</summary>
/// <remarks>
/// <b>Every read is scoped by product id, and that is not redundancy.</b> The
/// routes are <c>/admin/products/{productId}/media/{mediaId}</c>, and a media
/// id is a small integer someone can simply change. Looking a row up by id
/// alone and trusting the product id from the URL is how an admin editing one
/// product ends up deleting the hero image of another.
/// </remarks>
public interface IProductMediaRepository : IRepository<ProductMedia>
{
    /// <summary>Everything attached to a product, in display order.</summary>
    Task<IReadOnlyList<ProductMedia>> GetByProductAsync(
        long productId, CancellationToken cancellationToken = default);

    /// <summary>One row, only if it belongs to that product. Null otherwise.</summary>
    Task<ProductMedia?> GetForProductAsync(
        long productId, long mediaId, CancellationToken cancellationToken = default);

    /// <summary>The current hero image, if the product has one.</summary>
    Task<ProductMedia?> GetPrimaryAsync(
        long productId, CancellationToken cancellationToken = default);

    /// <summary>Highest sort order in use, or -1 when the product has no media.</summary>
    Task<int> MaxSortOrderAsync(long productId, CancellationToken cancellationToken = default);

    /// <summary>Everything on a product, tracked, for a bulk reorder.</summary>
    Task<IReadOnlyList<ProductMedia>> GetTrackedByProductAsync(
        long productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The image that should become primary once <paramref name="excludingMediaId"/>
    /// is gone: lowest sort order, ties broken on id.
    /// </summary>
    Task<ProductMedia?> GetPrimaryCandidateAsync(
        long productId, long excludingMediaId, CancellationToken cancellationToken = default);
}

public interface ICollectionRepository : IRepository<Collection>
{
    Task<Collection?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    Task<bool> SlugExistsAsync(
        string slug, long? excludingId = null, CancellationToken cancellationToken = default);
}
