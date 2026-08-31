using Microsoft.EntityFrameworkCore;
using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Repository.Interfaces.Catalog;

namespace WoodHeart.Repository.Repositories.Catalog;

public class CategoryRepository(DataContext context)
    : Repository<Category>(context), ICategoryRepository
{
    public async Task<Category?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        // EF.Property against the converted column, not `c.Slug == Slug.From(...)`.
        // The latter cannot be translated — Slug.From is a method call EF has no
        // SQL for — so it would silently fall back to loading every category and
        // filtering in memory.
        await Set.FirstOrDefaultAsync(
            c => EF.Property<string>(c, nameof(Category.Slug)) == slug, cancellationToken);

    public async Task<bool> SlugExistsAsync(
        string slug, long? excludingId = null, CancellationToken cancellationToken = default)
    {
        // Compares the converted column against a string rather than
        // materialising a Slug, so the check runs as SQL and can use the unique
        // index instead of loading every category.
        var query = Set.AsNoTracking().Where(c => EF.Property<string>(c, nameof(Category.Slug)) == slug);

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

    public async Task<Brand?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        await Set.FirstOrDefaultAsync(
            b => EF.Property<string>(b, nameof(Brand.Slug)) == slug, cancellationToken);

    public async Task<bool> SlugExistsAsync(
        string slug, long? excludingId = null, CancellationToken cancellationToken = default)
    {
        var query = Set.AsNoTracking().Where(b => EF.Property<string>(b, nameof(Brand.Slug)) == slug);

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
        string slug, CancellationToken cancellationToken = default) =>
        await WithDetails(Set.AsNoTracking())
            .FirstOrDefaultAsync(p => EF.Property<string>(p, nameof(Product.Slug)) == slug, cancellationToken);

    public async Task<Product?> GetByIdWithDetailsAsync(long id, CancellationToken cancellationToken = default) =>
        // Tracked: this is the admin edit path, and the caller writes back.
        await WithDetails(Set).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<bool> SlugExistsAsync(
        string slug, long? excludingId = null, CancellationToken cancellationToken = default)
    {
        var query = Set.AsNoTracking().Where(p => EF.Property<string>(p, nameof(Product.Slug)) == slug);

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
    public async Task<Collection?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        await Set.FirstOrDefaultAsync(
            c => EF.Property<string>(c, nameof(Collection.Slug)) == slug, cancellationToken);

    public async Task<bool> SlugExistsAsync(
        string slug, long? excludingId = null, CancellationToken cancellationToken = default)
    {
        var query = Set.AsNoTracking().Where(c => EF.Property<string>(c, nameof(Collection.Slug)) == slug);

        if (excludingId is { } id)
        {
            query = query.Where(c => c.Id != id);
        }

        return await query.AnyAsync(cancellationToken);
    }
}
