using Microsoft.EntityFrameworkCore;
using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.Enums.Catalog;
using WoodHeart.Repository.Interfaces.Catalog;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository.Queries;

namespace WoodHeart.Repository.Repositories.Catalog;

/// <summary>
/// Turns caller-supplied text into a <see cref="Slug"/> for querying.
/// </summary>
/// <remarks>
/// <para>
/// Comparisons go through a captured <see cref="Slug"/> rather than
/// <c>EF.Property&lt;string&gt;(x, "Slug")</c>. That form compiles and then
/// throws <c>InvalidCastException</c> the first time it runs, because the
/// model's property type is <see cref="Slug"/>, not <see cref="string"/> —
/// the converter is what makes it a string in the database. Comparing against
/// a captured value object lets EF apply the registered converter and emit a
/// plain <c>WHERE slug = @p</c>.
/// </para>
/// <para>
/// <see cref="Slug.From"/> throws on text that normalises to nothing, which a
/// URL can certainly contain. That is a 404, not a 500, so it is caught here
/// and reported as "no match" rather than escaping from a read.
/// </para>
/// </remarks>
internal static class SlugQuery
{
    public static bool TrySlug(string? value, out Slug slug)
    {
        try
        {
            slug = Slug.From(value ?? string.Empty);

            return true;
        }
        catch (ArgumentException)
        {
            slug = Slug.From("x");

            return false;
        }
    }
}


public class CategoryRepository(DataContext context)
    : Repository<Category>(context), ICategoryRepository
{
    public async Task<Category?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        if (!SlugQuery.TrySlug(slug, out var target))
        {
            return null;
        }

        return await Set.FirstOrDefaultAsync(c => c.Slug == target, cancellationToken);
    }

    public async Task<bool> SlugExistsAsync(
        string slug, long? excludingId = null, CancellationToken cancellationToken = default)
    {
        if (!SlugQuery.TrySlug(slug, out var target))
        {
            return false;
        }

        var query = Set.AsNoTracking().Where(c => c.Slug == target);

        if (excludingId is { } id)
        {
            query = query.Where(c => c.Id != id);
        }

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Category>> GetTreeAsync(
        bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var query = Set.AsNoTracking();

        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        // Ordered by path, so the flat result is already in depth-first order
        // and a caller can build the hierarchy in a single pass.
        return await query
            .OrderBy(c => c.MaterializedPath)
            .ThenBy(c => c.SortOrder)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Category>> GetSubtreeAsync(
        string materializedPath, CancellationToken cancellationToken = default) =>
        // Tracked, not AsNoTracking: the caller of this is moving or deleting
        // the subtree and needs to write the rows back.
        await Set
            .Where(c => c.MaterializedPath.StartsWith(materializedPath))
            .OrderBy(c => c.MaterializedPath)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Category>> GetAncestorsAsync(
        string materializedPath, CancellationToken cancellationToken = default)
    {
        // "/1/14/37/" -> [1, 14, 37]. The path already holds the whole chain,
        // which is the entire reason it is denormalised onto the row.
        var ids = materializedPath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => long.TryParse(segment, out var id) ? id : (long?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

        if (ids.Count == 0)
        {
            return [];
        }

        var found = await Set.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToListAsync(cancellationToken);

        // Ordered by the path, not by id. Ids ascend by creation order, so
        // sorting by them puts a category created before its own parent in the
        // wrong place in the breadcrumb.
        return [.. ids
            .Select(id => found.FirstOrDefault(c => c.Id == id))
            .Where(c => c is not null)
            .Select(c => c!)];
    }

    public async Task<bool> HasChildrenAsync(long categoryId, CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking().AnyAsync(c => c.ParentId == categoryId, cancellationToken);

    public async Task<bool> HasProductsAsync(long categoryId, CancellationToken cancellationToken = default) =>
        await Context.Products.AsNoTracking().AnyAsync(p => p.CategoryId == categoryId, cancellationToken);

    public async Task<int> MaxSortOrderAsync(long? parentId, CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking()
            .Where(c => c.ParentId == parentId)
            .Select(c => (int?)c.SortOrder)
            .MaxAsync(cancellationToken) ?? -1;

    public async Task<IReadOnlyDictionary<long, int>> GetProductCountsAsync(
        CancellationToken cancellationToken = default) =>
        await Context.Products.AsNoTracking()
            .GroupBy(p => p.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count, cancellationToken);
}

public class BrandRepository(DataContext context)
    : Repository<Brand>(context), IBrandRepository
{
    public async Task<IReadOnlyList<Brand>> GetAllAsync(
        bool includeInactive, CancellationToken cancellationToken = default)
    {
        var query = Set.AsNoTracking();

        if (!includeInactive)
        {
            query = query.Where(b => b.IsActive);
        }

        return await query.OrderBy(b => b.SortOrder).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<long, int>> GetProductCountsAsync(
        CancellationToken cancellationToken = default) =>
        await Context.Products.AsNoTracking()
            .Where(p => p.BrandId != null)
            .GroupBy(p => p.BrandId!.Value)
            .Select(g => new { BrandId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BrandId, x => x.Count, cancellationToken);

    public async Task<Brand?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        if (!SlugQuery.TrySlug(slug, out var target))
        {
            return null;
        }

        return await Set.FirstOrDefaultAsync(b => b.Slug == target, cancellationToken);
    }

    public async Task<bool> SlugExistsAsync(
        string slug, long? excludingId = null, CancellationToken cancellationToken = default)
    {
        if (!SlugQuery.TrySlug(slug, out var target))
        {
            return false;
        }

        var query = Set.AsNoTracking().Where(b => b.Slug == target);

        if (excludingId is { } id)
        {
            query = query.Where(b => b.Id != id);
        }

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<bool> HasProductsAsync(long brandId, CancellationToken cancellationToken = default) =>
        await Context.Products.AsNoTracking().AnyAsync(p => p.BrandId == brandId, cancellationToken);
}

public class ProductRepository(DataContext context)
    : Repository<Product>(context), IProductRepository
{
    public async Task<Product?> GetBySlugWithDetailsAsync(
        string slug, CancellationToken cancellationToken = default)
    {
        if (!SlugQuery.TrySlug(slug, out var target))
        {
            return null;
        }

        return await WithDetails(Set.AsNoTracking())
            .FirstOrDefaultAsync(p => p.Slug == target, cancellationToken);
    }

    public async Task<Product?> GetByIdWithDetailsAsync(long id, CancellationToken cancellationToken = default) =>
        // Tracked: this is the admin edit path, and the caller writes back.
        await WithDetails(Set).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<bool> SlugExistsAsync(
        string slug, long? excludingId = null, CancellationToken cancellationToken = default)
    {
        if (!SlugQuery.TrySlug(slug, out var target))
        {
            return false;
        }

        var query = Set.AsNoTracking().Where(p => p.Slug == target);

        if (excludingId is { } id)
        {
            query = query.Where(p => p.Id != id);
        }

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<bool> CodeExistsAsync(
        string code, long? excludingId = null, CancellationToken cancellationToken = default)
    {
        var query = Set.AsNoTracking().Where(p => p.Code == code);

        if (excludingId is { } id)
        {
            query = query.Where(p => p.Id != id);
        }

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<PagedList<Product>> SearchAsync(
        ProductQuery query, CancellationToken cancellationToken = default)
    {
        var products = Set.AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Brand)
            // Active variants and the primary image only. A listing needs the
            // cheapest price and one thumbnail per row; without these includes
            // VariantCount is always 0 and PrimaryImagePath always null, which
            // is silent rather than a failure.
            .Include(p => p.Variants.Where(v => v.IsActive && !v.IsDeleted))
            .Include(p => p.Media.Where(m => m.IsPrimary && !m.IsDeleted))
            // Split, so two collections do not multiply the rows: 20 products
            // with 5 variants and an image each is 100 rows carrying every
            // product's description five times over.
            .AsSplitQuery()
            .AsQueryable();

        if (query.CategoryId is { } categoryId)
        {
            if (query.IncludeDescendantCategories)
            {
                // Resolve the parent's path once, then match every category
                // whose path starts with it. One indexed prefix scan for the
                // whole subtree, rather than walking the tree in the service
                // and passing back a list of ids that grows without bound.
                var path = await Context.Categories.AsNoTracking()
                    .Where(c => c.Id == categoryId)
                    .Select(c => c.MaterializedPath)
                    .FirstOrDefaultAsync(cancellationToken);

                products = string.IsNullOrEmpty(path)
                    ? products.Where(p => false)
                    : products.Where(p => p.Category.MaterializedPath.StartsWith(path));
            }
            else
            {
                products = products.Where(p => p.CategoryId == categoryId);
            }
        }

        if (query.BrandId is { } brandId)
        {
            products = products.Where(p => p.BrandId == brandId);
        }

        if (query.CollectionId is { } collectionId)
        {
            products = products.Where(p => p.Collections.Any(c => c.Id == collectionId));
        }

        if (query.Status is { } status)
        {
            products = products.Where(p => p.Status == status);
        }

        if (query.ProductType is { } productType)
        {
            products = products.Where(p => p.ProductType == productType);
        }

        if (query.IsFeatured is { } featured)
        {
            products = products.Where(p => p.IsFeatured == featured);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = $"%{query.Search.Trim()}%";

            // ILike, not ToLower().Contains(): ILike maps to Postgres ILIKE,
            // which is index-assisted with pg_trgm and correct for Bangla.
            // Lower-casing both sides forces a sequential scan and mangles
            // scripts that have no case.
            // SearchText, not Name. Name is a LocalizedText stored as jsonb, and
            // EF cannot build a predicate inside a value-converted property:
            // EF.Property<string>(p, "Name") compiles and throws
            // InvalidCastException the first time it runs. SearchText is the
            // denormalised column DataContext maintains for exactly this.
            products = products.Where(p => EF.Functions.ILike(p.SearchText, term));
        }

        // Compares the converted decimal column, so this stays SQL. Money
        // itself never reaches the database as an object.
        if (query.MinPrice is { } min)
        {
            // A captured Money, so EF runs it through the registered converter
            // and compares decimals in SQL.
            var minimum = Money.Taka(min);

            products = products.Where(p => p.BasePrice >= minimum);
        }

        if (query.MaxPrice is { } max)
        {
            var maximum = Money.Taka(max);

            products = products.Where(p => p.BasePrice <= maximum);
        }

        products = query.SortBy switch
        {
            ProductSort.Oldest => products.OrderBy(p => p.CreatedAt),
            ProductSort.PriceLowToHigh =>
                products.OrderBy(p => p.BasePrice),
            ProductSort.PriceHighToLow =>
                products.OrderByDescending(p => p.BasePrice),
            // Nulls last: an unpublished draft should not head the list of what
            // is newest on the storefront.
            ProductSort.RecentlyPublished => products
                .OrderByDescending(p => p.PublishedAt.HasValue)
                .ThenByDescending(p => p.PublishedAt),
            ProductSort.Code => products.OrderBy(p => p.Code),
            _ => products.OrderByDescending(p => p.CreatedAt)
        };

        // Id as the final tiebreak on every sort. Without it two products with
        // the same price have no defined order, and a row can appear on page 1
        // and page 2 of the same result set while another never appears at all.
        products = ((IOrderedQueryable<Product>)products).ThenBy(p => p.Id);

        return await PagedList<Product>.CreateAsync(
            products, query.PageNumber, query.PageSize, cancellationToken);
    }

    public async Task<IReadOnlyList<Product>> GetRelatedAsync(
        long productId, long categoryId, int count, CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking()
            .Where(p => p.CategoryId == categoryId
                        && p.Id != productId
                        && p.Status == ProductStatus.Active)
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Variants.Where(v => v.IsActive && !v.IsDeleted))
            .Include(p => p.Media.Where(m => m.IsPrimary && !m.IsDeleted))
            .AsSplitQuery()
            .OrderBy(p => p.BasePrice)
            .ThenBy(p => p.Id)
            .Take(count)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Everything a product page needs, in one round trip.
    /// </summary>
    /// <remarks>
    /// <c>AsSplitQuery</c> because a product has two independent collections —
    /// variants and media. Loading both in one statement multiplies the rows
    /// (10 variants × 12 images is 120 rows carrying the product's description
    /// 120 times), and on a product with a long Bangla description that is
    /// megabytes over the wire to build one page.
    /// </remarks>
    private static IQueryable<Product> WithDetails(IQueryable<Product> query) =>
        query
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Variants.Where(v => !v.IsDeleted).OrderBy(v => v.SortOrder))
            .Include(p => p.Media.Where(m => !m.IsDeleted).OrderBy(m => m.SortOrder))
            .AsSplitQuery();
}

public class ProductVariantRepository(DataContext context)
    : Repository<ProductVariant>(context), IProductVariantRepository
{
    public async Task<ProductVariant?> GetDefaultAsync(
        long productId, CancellationToken cancellationToken = default) =>
        await Set.FirstOrDefaultAsync(
            v => v.ProductId == productId && v.IsDefault, cancellationToken);

    public async Task<int> MaxSortOrderAsync(long productId, CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking()
            .Where(v => v.ProductId == productId)
            .Select(v => (int?)v.SortOrder)
            .MaxAsync(cancellationToken) ?? -1;

    public async Task<bool> SkuExistsAsync(
        string sku, long? excludingId = null, CancellationToken cancellationToken = default)
    {
        var query = Set.AsNoTracking().Where(v => v.Sku == sku);

        if (excludingId is { } id)
        {
            query = query.Where(v => v.Id != id);
        }

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProductVariant>> GetByProductAsync(
        long productId, CancellationToken cancellationToken = default) =>
        await Set
            .Where(v => v.ProductId == productId)
            .OrderBy(v => v.SortOrder)
            .ToListAsync(cancellationToken);
}

public class CollectionRepository(DataContext context)
    : Repository<Collection>(context), ICollectionRepository
{
    public async Task<Collection?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        if (!SlugQuery.TrySlug(slug, out var target))
        {
            return null;
        }

        return await Set.FirstOrDefaultAsync(c => c.Slug == target, cancellationToken);
    }

    public async Task<bool> SlugExistsAsync(
        string slug, long? excludingId = null, CancellationToken cancellationToken = default)
    {
        if (!SlugQuery.TrySlug(slug, out var target))
        {
            return false;
        }

        var query = Set.AsNoTracking().Where(c => c.Slug == target);

        if (excludingId is { } id)
        {
            query = query.Where(c => c.Id != id);
        }

        return await query.AnyAsync(cancellationToken);
    }
}

public class ProductMediaRepository(DataContext context)
    : Repository<ProductMedia>(context), IProductMediaRepository
{
    public async Task<IReadOnlyList<ProductMedia>> GetByProductAsync(
        long productId, CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking()
            .Where(m => m.ProductId == productId)
            .OrderBy(m => m.SortOrder)
            // Ties broken on id, so the gallery order is the same on every
            // request. Without it two images sharing a sort order swap places
            // between page loads, and the "primary" one appears to move.
            .ThenBy(m => m.Id)
            .ToListAsync(cancellationToken);

    public async Task<ProductMedia?> GetForProductAsync(
        long productId, long mediaId, CancellationToken cancellationToken = default) =>
        // Tracked: every caller of this goes on to modify the row.
        await Set.FirstOrDefaultAsync(
            m => m.Id == mediaId && m.ProductId == productId, cancellationToken);

    public async Task<ProductMedia?> GetPrimaryAsync(
        long productId, CancellationToken cancellationToken = default) =>
        await Set.FirstOrDefaultAsync(
            m => m.ProductId == productId && m.IsPrimary, cancellationToken);

    public async Task<int> MaxSortOrderAsync(
        long productId, CancellationToken cancellationToken = default)
    {
        // -1 for "no media yet", so the caller's `max + 1` gives 0 for the
        // first image without a special case. MaxAsync on an empty sequence
        // throws for a non-nullable selector, which is why the cast is here.
        var max = await Set.AsNoTracking()
            .Where(m => m.ProductId == productId)
            .MaxAsync(m => (int?)m.SortOrder, cancellationToken);

        return max ?? -1;
    }

    public async Task<IReadOnlyList<ProductMedia>> GetTrackedByProductAsync(
        long productId, CancellationToken cancellationToken = default) =>
        await Set.Where(m => m.ProductId == productId)
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Id)
            .ToListAsync(cancellationToken);

    public async Task<ProductMedia?> GetPrimaryCandidateAsync(
        long productId, long excludingMediaId, CancellationToken cancellationToken = default) =>
        // Images only. Promoting a video to hero would put a card with no
        // photograph on it into every listing the product appears in.
        await Set.Where(m => m.ProductId == productId
                             && m.Id != excludingMediaId
                             && m.MediaType == MediaType.Image)
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Id)
            .FirstOrDefaultAsync(cancellationToken);
}
