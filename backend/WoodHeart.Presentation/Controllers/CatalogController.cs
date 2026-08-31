using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WoodHeart.Domain.Constants;
using WoodHeart.Presentation.Extensions;
using WoodHeart.Repository.Queries;
using WoodHeart.Service.Interfaces.Catalog;

namespace WoodHeart.Presentation.Controllers;

/// <summary>
/// The public catalog — the storefront's read API.
/// </summary>
/// <remarks>
/// <para>
/// <c>[AllowAnonymous]</c> is explicit rather than implied by the absence of
/// <c>[Authorize]</c>. If a global authorization fallback policy is ever added
/// — and one should be, so a controller cannot be left unprotected by
/// forgetting an attribute — this is the marker that says the storefront being
/// public is a decision, not an oversight.
/// </para>
/// <para>
/// Rate limited on the <c>Public</c> policy: generous, because this is ordinary
/// browsing, but not unlimited, because these are the endpoints a scraper
/// points at first.
/// </para>
/// </remarks>
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Public)]
[Route("api/catalog")]
public class CatalogController(IStorefrontService storefront) : BaseApiController
{
    /// <summary>The visible category tree, nested, with product counts.</summary>
    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories(CancellationToken cancellationToken) =>
        HandleResult(await storefront.GetCategoryTreeAsync(cancellationToken));

    /// <summary>
    /// A page of published products.
    /// </summary>
    /// <remarks>
    /// The bound <see cref="ProductQuery"/> carries a <c>Status</c> the caller
    /// can set. The service overwrites it — see <c>StorefrontService</c> — so
    /// <c>?status=Draft</c> returns published products rather than drafts.
    /// </remarks>
    [HttpGet("products")]
    public async Task<IActionResult> GetProducts(
        [FromQuery] ProductQuery query, CancellationToken cancellationToken)
    {
        var result = await storefront.SearchAsync(query, cancellationToken);

        if (result is { IsSuccess: true, Data: { } page })
        {
            Response.AddPaginationHeader(
                page.CurrentPage, page.PageSize, page.TotalCount, page.TotalPages);
        }

        return HandleResult(result);
    }

    /// <summary>
    /// One product page, addressed by slug.
    /// </summary>
    /// <remarks>
    /// By slug, not by id, because the slug is the URL. An id-addressed product
    /// page would need a redirect to the canonical URL on every request, or it
    /// would split the page's ranking across two addresses.
    /// </remarks>
    [HttpGet("products/{slug}")]
    public async Task<IActionResult> GetProduct(string slug, CancellationToken cancellationToken) =>
        HandleResult(await storefront.GetProductAsync(slug, cancellationToken));

    /// <summary>Other products in the same category.</summary>
    [HttpGet("products/{slug}/related")]
    public async Task<IActionResult> GetRelated(string slug, CancellationToken cancellationToken) =>
        HandleResult(await storefront.GetRelatedAsync(slug, cancellationToken));

    [HttpGet("collections/{slug}")]
    public async Task<IActionResult> GetCollection(string slug, CancellationToken cancellationToken) =>
        HandleResult(await storefront.GetCollectionAsync(slug, cancellationToken));

    [HttpGet("collections/{slug}/products")]
    public async Task<IActionResult> GetCollectionProducts(
        string slug, [FromQuery] ProductQuery query, CancellationToken cancellationToken)
    {
        var result = await storefront.GetCollectionProductsAsync(slug, query, cancellationToken);

        if (result is { IsSuccess: true, Data: { } page })
        {
            Response.AddPaginationHeader(
                page.CurrentPage, page.PageSize, page.TotalCount, page.TotalPages);
        }

        return HandleResult(result);
    }
}
