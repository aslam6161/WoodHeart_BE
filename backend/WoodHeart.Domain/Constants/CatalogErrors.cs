namespace WoodHeart.Domain.Constants;

/// <summary>
/// Stable error codes for the catalog module.
/// </summary>
/// <remarks>
/// <para>
/// <b>The code is the contract; the message is not.</b> The Angular client
/// branches on <c>catalog.category_not_found</c>. The English message beside it
/// gets reworded and translated to Bangla, and any client matching on prose
/// breaks silently the first time someone improves the wording.
/// </para>
/// <para>
/// The suffix decides the HTTP status — <c>BaseApiController.HandleResult</c>
/// maps <c>.not_found</c> to 404 and <c>.conflict</c> or <c>_taken</c> to 409 —
/// so the naming is load-bearing rather than decorative.
/// </para>
/// </remarks>
public static class CatalogErrors
{
    private const string Prefix = "catalog.";

    // --- Category ------------------------------------------------------------

    public const string CategoryNotFound = Prefix + "category.not_found";
    public const string ParentCategoryNotFound = Prefix + "parent_category.not_found";
    public const string CategorySlugTaken = Prefix + "category_slug_taken";

    /// <summary>
    /// Moving a category under one of its own descendants. Allowing it would
    /// detach the subtree from the root entirely — the rows survive, and
    /// nothing can reach them.
    /// </summary>
    public const string CategoryCycle = Prefix + "category.cycle.conflict";

    public const string CategoryHasChildren = Prefix + "category.has_children.conflict";
    public const string CategoryHasProducts = Prefix + "category.has_products.conflict";

    // --- Brand ---------------------------------------------------------------

    public const string BrandNotFound = Prefix + "brand.not_found";
    public const string BrandSlugTaken = Prefix + "brand_slug_taken";
    public const string BrandHasProducts = Prefix + "brand.has_products.conflict";

    // --- Product -------------------------------------------------------------

    public const string ProductNotFound = Prefix + "product.not_found";
    public const string ProductSlugTaken = Prefix + "product_slug_taken";
    public const string ProductCodeTaken = Prefix + "product_code_taken";

    // --- Variant -------------------------------------------------------------

    public const string VariantNotFound = Prefix + "variant.not_found";
    public const string VariantSkuTaken = Prefix + "variant_sku_taken";

    // --- Collection ----------------------------------------------------------

    public const string CollectionNotFound = Prefix + "collection.not_found";
    public const string CollectionSlugTaken = Prefix + "collection_slug_taken";

    // --- Shared --------------------------------------------------------------

    /// <summary>Text supplied that cannot be reduced to a URL-safe slug.</summary>
    public const string SlugNotDerivable = Prefix + "slug_not_derivable";
}
