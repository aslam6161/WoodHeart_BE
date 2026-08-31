using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Domain.Entity.Catalog;

/// <summary>
/// A node in the admin-managed category tree — Living Room → Sofas →
/// L-Shaped Sofas.
/// </summary>
/// <remarks>
/// <para>
/// <b>Adjacency list, not nested sets.</b> A parent pointer makes every write
/// cheap and every edit local, at the cost of recursive reads. That is the
/// right trade here: this tree is read constantly and edited rarely, and the
/// reads are answered from a recursive CTE and then cached. Nested sets make
/// the reads trivial and turn "drag one category under another" into a
/// renumbering of the entire table.
/// </para>
/// <para>
/// <b>Why depth is unlimited.</b> The initial tree in PLAN.md §2 is seed data,
/// not a schema. A fixed two-level structure is the kind of decision that looks
/// fine until the day someone needs Bedroom → Beds → King → Storage Beds.
/// </para>
/// <para>
/// <b><see cref="MaterializedPath"/> is a cache, not the truth.</b> The parent
/// pointer is authoritative; the path is a denormalised <c>/1/14/37/</c> string
/// maintained on write so that "everything under Living Room" is one indexed
/// <c>LIKE</c> rather than a recursive query on a hot path. Whenever a category
/// moves, the paths of its whole subtree are rewritten — see the service that
/// owns the move.
/// </para>
/// </remarks>
public class Category : SoftDeletableEntity
{
    /// <summary>Display name, English required and Bangla optional.</summary>
    public LocalizedText Name { get; set; } = null!;

    /// <summary>
    /// The URL segment. Stable once published — changing it breaks every
    /// inbound link and every share.
    /// </summary>
    public Slug Slug { get; set; } = null!;

    public LocalizedText? Description { get; set; }

    /// <summary><c>null</c> for a root category.</summary>
    public long? ParentId { get; set; }

    public Category? Parent { get; set; }

    public ICollection<Category> Children { get; set; } = [];

    /// <summary>
    /// Denormalised ancestor path, <c>/1/14/37/</c>, always ending in this
    /// category's own id. Rebuilt whenever the category moves.
    /// </summary>
    public string MaterializedPath { get; set; } = string.Empty;

    /// <summary>Zero for a root. Derived from the path, stored to avoid computing it on read.</summary>
    public int Depth { get; set; }

    /// <summary>Manual ordering within a parent. Ties break on name.</summary>
    public int SortOrder { get; set; }

    /// <summary>Hides the category and everything under it from the public API.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Surfaces the category on the home page.</summary>
    public bool IsFeatured { get; set; }

    /// <summary>Relative storage key for the category tile, not a full URL.</summary>
    public string? ImagePath { get; set; }

    public string? SeoTitle { get; set; }

    public string? SeoDescription { get; set; }

    public ICollection<Product> Products { get; set; } = [];

    /// <summary>Builds the path this category should have under a given parent.</summary>
    /// <remarks>
    /// Called after the id is known, so a new category is saved once to obtain
    /// its id and then updated. Slightly awkward, and much less awkward than
    /// generating keys client-side purely to avoid it.
    /// </remarks>
    public string BuildPath(Category? parent) =>
        parent is null ? $"/{Id}/" : $"{parent.MaterializedPath}{Id}/";

    /// <summary>True when this category is the given one, or sits beneath it.</summary>
    public bool IsWithin(Category ancestor) =>
        MaterializedPath.StartsWith(ancestor.MaterializedPath, StringComparison.Ordinal);
}
