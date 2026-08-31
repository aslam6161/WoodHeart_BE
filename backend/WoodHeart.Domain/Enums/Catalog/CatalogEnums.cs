namespace WoodHeart.Domain.Enums.Catalog;

/// <summary>
/// How a product is fulfilled. This drives stock behaviour, so it is not
/// cosmetic.
/// </summary>
public enum ProductType
{
    /// <summary>Held in a warehouse. Reserves stock at checkout.</summary>
    Stocked = 0,

    /// <summary>
    /// Built after the order is placed. Skips stock reservation entirely and
    /// carries <c>LeadTimeDays</c> onto the order line instead — a made-to-order
    /// wardrobe has no "on hand" quantity to draw down.
    /// </summary>
    MadeToOrder = 1,

    /// <summary>
    /// A service rather than a thing: delivery, assembly, an interior
    /// consultation. No stock, no dimensions, no delivery surcharge.
    /// </summary>
    Service = 2
}

/// <summary>
/// The publication state of a product.
/// </summary>
/// <remarks>
/// Deliberately separate from <c>IsDeleted</c>. Archiving takes a product off
/// the storefront while keeping it joinable from historical orders; deleting is
/// for a row created by mistake. A product that has ever been ordered should be
/// archived, never deleted.
/// </remarks>
public enum ProductStatus
{
    /// <summary>Being written. Invisible to the public API at any URL.</summary>
    Draft = 0,

    /// <summary>Live on the storefront.</summary>
    Active = 1,

    /// <summary>Withdrawn from sale but still resolvable for old orders.</summary>
    Archived = 2
}

/// <summary>
/// What a piece of product media actually is.
/// </summary>
public enum MediaType
{
    Image = 0,

    /// <summary>An externally hosted clip — YouTube or Facebook, not our storage.</summary>
    Video = 1,

    /// <summary>Care instructions, a warranty card, an assembly guide.</summary>
    Document = 2
}
