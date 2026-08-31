using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Domain.Entity.Catalog;

/// <summary>
/// A curated set of products — "Shop the Bedroom", "Minimalist Living",
/// "Eid Collection".
/// </summary>
/// <remarks>
/// <para>
/// <b>Not a second category tree.</b> A category answers "what is this?" and a
/// product has exactly one. A collection answers "what goes together?" and a
/// product can be in many, or none. The bed, the side table and the lamp in one
/// room shot belong to three different categories and one collection.
/// </para>
/// <para>
/// PLAN.md §6.1 calls this out as how interior brands actually sell: cheap to
/// build, high conversion. It is also the natural home for seasonal
/// merchandising, which is why it carries a scheduling window.
/// </para>
/// </remarks>
public class Collection : SoftDeletableEntity
{
    public LocalizedText Name { get; set; } = null!;

    public Slug Slug { get; set; } = null!;

    public LocalizedText? Description { get; set; }

    /// <summary>Wide banner for the collection landing page.</summary>
    public string? BannerPath { get; set; }

    /// <summary>Square tile for collection grids.</summary>
    public string? ThumbnailPath { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsFeatured { get; set; }

    public int SortOrder { get; set; }

    // --- Scheduling ----------------------------------------------------------
    // An Eid collection should appear and disappear on its own. Both null means
    // always on. Evaluated against the injected clock, never DateTime.UtcNow,
    // so "this collection goes live next Tuesday" is testable.

    public DateTimeOffset? StartsAt { get; set; }

    public DateTimeOffset? EndsAt { get; set; }

    public string? SeoTitle { get; set; }

    public string? SeoDescription { get; set; }

    public ICollection<Product> Products { get; set; } = [];

    /// <summary>Whether the collection is live at the given moment.</summary>
    /// <remarks>
    /// Takes the time as an argument rather than reading a clock, so the caller
    /// supplies <c>IDateTimeProvider.UtcNow</c> and a test can supply anything.
    /// </remarks>
    public bool IsLiveAt(DateTimeOffset moment) =>
        IsActive
        && !IsDeleted
        && (StartsAt is null || StartsAt <= moment)
        && (EndsAt is null || EndsAt > moment);
}
