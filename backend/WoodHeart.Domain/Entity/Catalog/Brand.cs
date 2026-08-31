using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Domain.Entity.Catalog;

/// <summary>
/// A manufacturer or sub-brand — WoodHeart's own line, or a supplier's.
/// </summary>
/// <remarks>
/// Optional on a product. Most of what WoodHeart sells is its own, so forcing a
/// brand onto every row would mean a "WoodHeart" brand on 90% of the catalog
/// carrying no information. It earns its place as a filter facet the moment a
/// second supplier's furniture is listed.
/// </remarks>
public class Brand : SoftDeletableEntity
{
    public LocalizedText Name { get; set; } = null!;

    public Slug Slug { get; set; } = null!;

    public LocalizedText? Description { get; set; }

    /// <summary>Relative storage key, not a full URL.</summary>
    public string? LogoPath { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public ICollection<Product> Products { get; set; } = [];
}
