using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Catalog;
using WoodHeart.Service.DTOs.Catalog;
using WoodHeart.Service.Interfaces.Catalog;
using WoodHeart.Service.Mapping.Catalog;

namespace WoodHeart.Service.Services.Catalog;

/// <summary>
/// The category tree: reads, edits, and the two operations that have real
/// invariants — moving a subtree and deleting a node.
/// </summary>
/// <remarks>
/// <para>
/// This service owns the <c>MaterializedPath</c> invariant. The parent pointer
/// is the truth; the path is a denormalised cache of the ancestor chain, and it
/// is only correct because every write that could invalidate it goes through
/// here and rewrites the affected subtree in the same transaction.
/// </para>
/// <para>
/// Business failures are <see cref="GeneralResponse"/> values, not exceptions.
/// "You cannot delete a category that still has products" is not exceptional —
/// it is an admin clicking the wrong button, and it deserves a message rather
/// than a stack trace.
/// </para>
/// </remarks>
public class CategoryService(
    ICategoryRepository categories,
    IUnitOfWork unitOfWork) : ICategoryService
{
    public async Task<GeneralResponse<IReadOnlyList<CategoryTreeDto>>> GetTreeAsync(
        bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var flat = await categories.GetTreeAsync(includeInactive, cancellationToken);

        // One grouped query for every category, not one per node. The
        // aggregation lives in the repository so this service holds no EF
        // dependency and can be tested against a substitute.
        var counts = await categories.GetProductCountsAsync(cancellationToken);

        var dtos = flat.Select(c =>
        {
            var dto = CatalogMapper.ToTreeDto(c);
            dto.ProductCount = counts.GetValueOrDefault(c.Id);
            return dto;
        }).ToList();

        return GeneralResponse<IReadOnlyList<CategoryTreeDto>>.Success(BuildHierarchy(dtos));
    }

    public async Task<GeneralResponse<CategoryDto>> GetByIdAsync(
        long id, CancellationToken cancellationToken = default)
    {
        var category = await categories.GetByIdAsync(id, cancellationToken);

        return category is null
            ? NotFound(id)
            : GeneralResponse<CategoryDto>.Success(CatalogMapper.ToDto(category));
    }

    public async Task<GeneralResponse<CategoryDto>> GetBySlugAsync(
        string slug, CancellationToken cancellationToken = default)
    {
        var category = await categories.GetBySlugAsync(slug, cancellationToken);

        return category is null
            ? GeneralResponse<CategoryDto>.Fail(
                CatalogErrors.CategoryNotFound, $"No category with the slug '{slug}'.")
            : GeneralResponse<CategoryDto>.Success(CatalogMapper.ToDto(category));
    }

    public async Task<GeneralResponse<CategoryDto>> CreateAsync(
        CreateCategoryDto dto, CancellationToken cancellationToken = default)
    {
        if (!TryBuildSlug(dto.Slug, dto.NameEn, out var slug, out var slugFailure))
        {
            return slugFailure!;
        }

        if (await categories.SlugExistsAsync(slug!.Value, null, cancellationToken))
        {
            return SlugTaken(slug.Value);
        }

        Category? parent = null;

        if (dto.ParentId is { } parentId)
        {
            parent = await categories.GetByIdAsync(parentId, cancellationToken);

            if (parent is null)
            {
                return GeneralResponse<CategoryDto>.Fail(
                    CatalogErrors.ParentCategoryNotFound, $"No parent category with id {parentId}.");
            }
        }

        var category = new Category
        {
            Name = LocalizedText.Create(dto.NameEn, dto.NameBn),
            Slug = slug,
            Description = string.IsNullOrWhiteSpace(dto.DescriptionEn)
                ? null
                : LocalizedText.Create(dto.DescriptionEn, dto.DescriptionBn),
            ParentId = dto.ParentId,
            Depth = parent is null ? 0 : parent.Depth + 1,
            SortOrder = await categories.MaxSortOrderAsync(dto.ParentId, cancellationToken) + 1,
            IsActive = dto.IsActive,
            IsFeatured = dto.IsFeatured,
            ImagePath = dto.ImagePath,
            SeoTitle = dto.SeoTitle,
            SeoDescription = dto.SeoDescription
        };

        // Two saves inside one transaction, because the path contains the row's
        // own id and the database assigns it. The alternative is generating keys
        // client-side purely to avoid a second write, which is a worse trade.
        return await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await categories.InsertAsync(category, ct);
            await unitOfWork.SaveChangesAsync(ct);

            category.MaterializedPath = category.BuildPath(parent);
            categories.Update(category);
            await unitOfWork.SaveChangesAsync(ct);

            return GeneralResponse<CategoryDto>.Success(
                CatalogMapper.ToDto(category), "Category created.", category.Id);
        }, cancellationToken);
    }

    public async Task<GeneralResponse<CategoryDto>> UpdateAsync(
        long id, UpdateCategoryDto dto, CancellationToken cancellationToken = default)
    {
        var category = await categories.GetByIdAsync(id, cancellationToken);

        if (category is null)
        {
            return NotFound(id);
        }

        // Only touch the slug when one was actually supplied. Silently
        // regenerating it from a renamed category would move a published URL
        // and break every inbound link to it.
        if (!string.IsNullOrWhiteSpace(dto.Slug))
        {
            if (!TryBuildSlug(dto.Slug, dto.NameEn, out var slug, out var slugFailure))
            {
                return slugFailure!;
            }

            if (slug!.Value != category.Slug.Value)
            {
                if (await categories.SlugExistsAsync(slug.Value, id, cancellationToken))
                {
                    return SlugTaken(slug.Value);
                }

                category.Slug = slug;
            }
        }

        category.Name = LocalizedText.Create(dto.NameEn, dto.NameBn);
        category.Description = string.IsNullOrWhiteSpace(dto.DescriptionEn)
            ? null
            : LocalizedText.Create(dto.DescriptionEn, dto.DescriptionBn);
        category.IsActive = dto.IsActive;
        category.IsFeatured = dto.IsFeatured;
        category.ImagePath = dto.ImagePath;
        category.SeoTitle = dto.SeoTitle;
        category.SeoDescription = dto.SeoDescription;

        categories.Update(category);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return GeneralResponse<CategoryDto>.Success(CatalogMapper.ToDto(category), "Category updated.", id);
    }

    public async Task<GeneralResponse<CategoryDto>> MoveAsync(
        long id, MoveCategoryDto dto, CancellationToken cancellationToken = default)
    {
        var category = await categories.GetByIdAsync(id, cancellationToken);

        if (category is null)
        {
            return NotFound(id);
        }

        Category? newParent = null;

        if (dto.NewParentId is { } parentId)
        {
            if (parentId == id)
            {
                return GeneralResponse<CategoryDto>.Fail(
                    CatalogErrors.CategoryCycle, "A category cannot be its own parent.");
            }

            newParent = await categories.GetByIdAsync(parentId, cancellationToken);

            if (newParent is null)
            {
                return GeneralResponse<CategoryDto>.Fail(
                    CatalogErrors.ParentCategoryNotFound, $"No parent category with id {parentId}.");
            }

            // The check that matters. Moving Living Room under Sofas — which
            // sits inside Living Room — detaches the whole branch from the root:
            // the rows still exist and nothing can navigate to them, and the
            // materialized paths become mutually recursive nonsense.
            if (newParent.IsWithin(category))
            {
                return GeneralResponse<CategoryDto>.Fail(
                    CatalogErrors.CategoryCycle,
                    "A category cannot be moved beneath one of its own descendants.");
            }
        }

        return await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            // Fetch the subtree before changing anything — the query keys off
            // the old path, and it is about to be rewritten.
            var subtree = await categories.GetSubtreeAsync(category.MaterializedPath, ct);
            var oldPath = category.MaterializedPath;
            var oldDepth = category.Depth;

            category.ParentId = dto.NewParentId;
            category.Depth = newParent is null ? 0 : newParent.Depth + 1;
            category.SortOrder = dto.SortOrder
                ?? await categories.MaxSortOrderAsync(dto.NewParentId, ct) + 1;
            category.MaterializedPath = category.BuildPath(newParent);

            // Every descendant's path starts with the moved category's old path.
            // Swapping that prefix for the new one moves the entire branch in
            // one pass, and every descendant shifts by the same depth delta —
            // their position relative to the moved node has not changed.
            var depthDelta = category.Depth - oldDepth;

            foreach (var descendant in subtree.Where(c => c.Id != category.Id))
            {
                descendant.MaterializedPath =
                    string.Concat(category.MaterializedPath, descendant.MaterializedPath[oldPath.Length..]);
                descendant.Depth += depthDelta;
            }

            categories.Update(category);
            categories.UpdateRange(subtree.Where(c => c.Id != category.Id));

            await unitOfWork.SaveChangesAsync(ct);

            return GeneralResponse<CategoryDto>.Success(
                CatalogMapper.ToDto(category), "Category moved.", category.Id);
        }, cancellationToken);
    }

    public async Task<GeneralResponse> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var category = await categories.GetByIdAsync(id, cancellationToken);

        if (category is null)
        {
            return GeneralResponse.Fail(CatalogErrors.CategoryNotFound, $"No category with id {id}.");
        }

        // Refuse rather than cascade. Soft-deleting a parent would leave its
        // children live but unreachable — the global query filter hides the
        // parent, so the subtree simply vanishes from the tree with no error
        // and no obvious way to get it back.
        if (await categories.HasChildrenAsync(id, cancellationToken))
        {
            return GeneralResponse.Fail(
                CatalogErrors.CategoryHasChildren,
                "Move or delete the subcategories first.");
        }

        if (await categories.HasProductsAsync(id, cancellationToken))
        {
            return GeneralResponse.Fail(
                CatalogErrors.CategoryHasProducts,
                "Move the products in this category to another one first.");
        }

        categories.Delete(category);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return GeneralResponse.Success("Category deleted.", id);
    }

    // -------------------------------------------------------------------------

    /// <summary>
    /// Turns the flat, path-ordered list into a hierarchy in one pass.
    /// </summary>
    /// <remarks>
    /// A node whose parent is not in the list becomes a root. That happens
    /// legitimately when inactive categories are filtered out and a live child
    /// has a hidden parent — showing it at the top beats dropping it silently
    /// from the admin tree, where it would look deleted.
    /// </remarks>
    private static List<CategoryTreeDto> BuildHierarchy(List<CategoryTreeDto> flat)
    {
        var byId = flat.ToDictionary(c => c.Id);
        var roots = new List<CategoryTreeDto>();

        foreach (var node in flat)
        {
            if (node.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent))
            {
                parent.Children.Add(node);
            }
            else
            {
                roots.Add(node);
            }
        }

        SortSiblings(roots);

        return roots;
    }

    /// <summary>
    /// Orders every sibling list by the admin's manual sort order, ties broken
    /// on name.
    /// </summary>
    /// <remarks>
    /// Applied at every level, not just the roots. The repository returns rows
    /// ordered by materialized path, which for siblings is id order — so
    /// without this, dragging a child category into position would appear to
    /// work in the admin UI and have no effect anywhere the tree is rendered.
    /// </remarks>
    private static void SortSiblings(List<CategoryTreeDto> nodes)
    {
        nodes.Sort(static (left, right) => left.SortOrder != right.SortOrder
            ? left.SortOrder.CompareTo(right.SortOrder)
            : string.CompareOrdinal(left.NameEn, right.NameEn));

        foreach (var node in nodes)
        {
            SortSiblings(node.Children);
        }
    }

    /// <summary>
    /// Builds a slug from the supplied text, or from the name when none was
    /// given.
    /// </summary>
    /// <remarks>
    /// <see cref="Slug.From"/> throws when nothing survives normalisation —
    /// a name of only punctuation, say. That is a validation failure caused by
    /// user input, so it becomes a <see cref="GeneralResponse"/> rather than
    /// escaping as a 500.
    /// </remarks>
    private static bool TryBuildSlug(
        string? supplied, string fallback, out Slug? slug, out GeneralResponse<CategoryDto>? failure)
    {
        try
        {
            slug = Slug.From(string.IsNullOrWhiteSpace(supplied) ? fallback : supplied);
            failure = null;

            return true;
        }
        catch (ArgumentException)
        {
            slug = null;
            failure = GeneralResponse<CategoryDto>.Fail(
                CatalogErrors.SlugNotDerivable,
                "The name does not contain any characters usable in a URL. Supply a slug explicitly.");

            return false;
        }
    }

    private static GeneralResponse<CategoryDto> NotFound(long id) =>
        GeneralResponse<CategoryDto>.Fail(CatalogErrors.CategoryNotFound, $"No category with id {id}.");

    private static GeneralResponse<CategoryDto> SlugTaken(string slug) =>
        GeneralResponse<CategoryDto>.Fail(
            CatalogErrors.CategorySlugTaken, $"The slug '{slug}' is already in use.");
}
