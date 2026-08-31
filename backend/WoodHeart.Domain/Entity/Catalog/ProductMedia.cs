using WoodHeart.Domain.Enums.Catalog;

namespace WoodHeart.Domain.Entity.Catalog;

/// <summary>
/// An image, video or document attached to a product, and optionally to one
/// specific variant.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a nullable <see cref="VariantId"/> rather than two tables.</b> Most
/// photos belong to the product — the room shot, the detail of the joinery. A
/// few belong to one variant: the Segun finish looks nothing like the Mehogoni,
/// and showing the wrong wood after a customer picks one is the sort of thing
/// that generates a return. One table with an optional variant pointer gives
/// the gallery a single ordered query; two tables would need a union and a
/// merged sort on every product page.
/// </para>
/// <para>
/// <b><see cref="StoragePath"/> is a key, not a URL.</b> It holds
/// <c>products/2026/08/a1b2c3.webp</c>, and the API composes the public URL
/// from the CDN base at read time. Storing absolute URLs bakes today's host
/// into every row, and moving to R2 or Spaces then becomes a data migration
/// rather than a configuration change.
/// </para>
/// </remarks>
public class ProductMedia : SoftDeletableEntity
{
    public long ProductId { get; set; }

    public Product Product { get; set; } = null!;

    /// <summary>
    /// When set, this media only shows once that variant is selected. Null means
    /// it belongs to the product and always shows.
    /// </summary>
    public long? VariantId { get; set; }

    public ProductVariant? Variant { get; set; }

    public MediaType MediaType { get; set; } = MediaType.Image;

    /// <summary>Storage key relative to the media root. Never an absolute URL.</summary>
    public string StoragePath { get; set; } = null!;

    /// <summary>
    /// Alt text. Required in practice for images — it is what a screen reader
    /// announces and what Google reads, and an empty one is a silent
    /// accessibility and SEO failure on the page that sells the product.
    /// </summary>
    public string? AltText { get; set; }

    /// <summary>Optional visible caption, distinct from alt text.</summary>
    public string? Caption { get; set; }

    /// <summary>
    /// The hero image. One per product, enforced by a filtered unique index —
    /// it is what appears on cards, in search results and on shares.
    /// </summary>
    public bool IsPrimary { get; set; }

    public int SortOrder { get; set; }

    // --- Intrinsic dimensions ------------------------------------------------
    // Recorded at upload so the storefront can set width and height on the img
    // tag. Without them the page reflows as each image loads, which is a
    // Cumulative Layout Shift penalty on exactly the pages that need to rank.

    public int? Width { get; set; }

    public int? Height { get; set; }

    public long? FileSizeBytes { get; set; }

    /// <summary>MIME type recorded at upload, e.g. <c>image/webp</c>.</summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// For <see cref="MediaType.Video"/>: the external URL. Videos are not
    /// hosted by us — a VPS in Dhaka is the wrong place to serve video from.
    /// </summary>
    public string? ExternalUrl { get; set; }
}
