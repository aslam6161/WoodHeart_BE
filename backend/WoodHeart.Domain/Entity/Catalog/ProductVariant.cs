using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Domain.Entity.Catalog;

/// <summary>
/// A specific, buyable configuration of a product — Segun / 6ft / Matte.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the thing that is actually sold.</b> Price, SKU, barcode and
/// stock all live here, not on <see cref="Product"/>. Carts hold variant ids,
/// order lines snapshot variant details, and the stock ledger moves against
/// variants. A product with no options still has exactly one variant.
/// </para>
/// <para>
/// <b><see cref="OptionValues"/> is JSONB, not an option/value table pair.</b>
/// The relational modelling — <c>Option</c>, <c>OptionValue</c>,
/// <c>VariantOptionValue</c> — is three tables and two joins to answer "what
/// wood is this?", and every faceted filter becomes a join per facet. JSONB
/// with a GIN index answers <c>OptionValues @&gt; '{"Wood":"Segun"}'</c>
/// directly, which is exactly the shape of a storefront filter, and adding an
/// option type needs no migration. PLAN.md §11 commits to this.
/// </para>
/// <para>
/// The cost is real and worth naming: no database-level guarantee that two
/// variants of one product use the same option keys, and no foreign key onto a
/// list of valid values. That consistency is enforced in the catalog service,
/// which is where a helpful error message can be produced anyway.
/// </para>
/// </remarks>
public class ProductVariant : SoftDeletableEntity
{
    public long ProductId { get; set; }

    public Product Product { get; set; } = null!;

    /// <summary>
    /// What warehouse staff pick by, and what appears on the invoice line.
    /// Unique across the catalog.
    /// </summary>
    public string Sku { get; set; } = null!;

    /// <summary>
    /// Human-readable summary of the options, "Segun · 6ft · Matte". Denormalised
    /// so an order line, a picking list or an SMS can name the variant without
    /// reconstructing it from JSON.
    /// </summary>
    public string VariantName { get; set; } = null!;

    /// <summary>
    /// The option combination, <c>{"Wood":"Segun","Size":"6ft"}</c>. Keys are
    /// option names, values are the chosen value. GIN indexed for filtering.
    /// </summary>
    public Dictionary<string, string> OptionValues { get; set; } = [];

    /// <summary>
    /// Overrides <see cref="Product.BasePrice"/> when set. Null means "same as
    /// the product", which keeps a price change on a single-variant product to
    /// one edit rather than two rows that can disagree.
    /// </summary>
    public Money? PriceOverride { get; set; }

    /// <summary>Per-variant "was" price. Falls back to the product's.</summary>
    public Money? CompareAtPriceOverride { get; set; }

    /// <summary>EAN or UPC, where the supplier provides one.</summary>
    public string? Barcode { get; set; }

    /// <summary>
    /// Physical overrides. A 7ft bed weighs more than the 6ft the product
    /// describes; null means "inherit from the product".
    /// </summary>
    public decimal? WeightKg { get; set; }

    public decimal? LengthCm { get; set; }

    public decimal? WidthCm { get; set; }

    public decimal? HeightCm { get; set; }

    /// <summary>
    /// The variant a product page opens on. Exactly one per product should be
    /// flagged; enforced by a filtered unique index.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>Ordering in the variant picker.</summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Lets a single configuration be withdrawn — 7ft discontinued — without
    /// touching the product or the other variants.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public ICollection<ProductMedia> Media { get; set; } = [];

    /// <summary>
    /// What the customer pays before discounts: the override if present,
    /// otherwise the product's base price.
    /// </summary>
    /// <remarks>
    /// Requires <see cref="Product"/> to be loaded when
    /// <see cref="PriceOverride"/> is null. Every query that reads a price
    /// includes the product, and this throws rather than silently returning
    /// zero if one does not — a price that quietly becomes 0.00 is worse than
    /// an exception.
    /// </remarks>
    public Money EffectivePrice =>
        PriceOverride
        ?? Product?.BasePrice
        ?? throw new InvalidOperationException(
            $"Variant {Sku} has no price override and its product was not loaded.");

    public Money? EffectiveCompareAtPrice => CompareAtPriceOverride ?? Product?.CompareAtPrice;

    /// <summary>True when there is a "was" price genuinely above the current one.</summary>
    public bool IsOnOffer =>
        EffectiveCompareAtPrice is { } was && was > EffectivePrice;
}
