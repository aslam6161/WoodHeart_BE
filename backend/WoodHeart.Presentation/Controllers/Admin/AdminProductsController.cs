using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoodHeart.Domain.Constants;
using WoodHeart.Presentation.Extensions;
using WoodHeart.Repository.Queries;
using WoodHeart.Service.DTOs.Catalog;
using WoodHeart.Service.Interfaces.Catalog;

namespace WoodHeart.Presentation.Controllers.Admin;

/// <summary>Product and variant management for the admin panel.</summary>
[Authorize(Policy = Policies.RequireAdminOrManager)]
[Route("api/admin/products")]
public class AdminProductsController(IProductService products) : BaseApiController
{
    /// <summary>
    /// A filtered, sorted page of products.
    /// </summary>
    /// <remarks>
    /// The paging metadata goes out in <c>X-Pagination</c> rather than wrapping
    /// the array, so the body stays a clean list. That header only reaches the
    /// browser because it is named in <c>Access-Control-Expose-Headers</c> —
    /// remove it there and every pager silently shows a single page.
    /// </remarks>
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] ProductQuery query, CancellationToken cancellationToken)
    {
        var result = await products.SearchAsync(query, cancellationToken);

        if (result is { IsSuccess: true, Data: { } page })
        {
            Response.AddPaginationHeader(
                page.CurrentPage, page.PageSize, page.TotalCount, page.TotalPages);
        }

        return HandleResult(result);
    }

    [HttpGet("{id:long}", Name = nameof(GetProductById))]
    public async Task<IActionResult> GetProductById(long id, CancellationToken cancellationToken) =>
        HandleResult(await products.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(CreateProductDto dto, CancellationToken cancellationToken)
    {
        var result = await products.CreateAsync(dto, cancellationToken);

        return HandleCreated(result, nameof(GetProductById), new { id = result.Id });
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(
        long id, UpdateProductDto dto, CancellationToken cancellationToken) =>
        HandleResult(await products.UpdateAsync(id, dto, cancellationToken));

    /// <summary>
    /// Publishes or withdraws a product.
    /// </summary>
    /// <remarks>
    /// Its own endpoint so going live is a deliberate act rather than a side
    /// effect of saving a draft — and so the permission to publish can be
    /// separated from the permission to edit later without moving the route.
    /// </remarks>
    [HttpPost("{id:long}/status")]
    public async Task<IActionResult> ChangeStatus(
        long id, ChangeProductStatusDto dto, CancellationToken cancellationToken) =>
        HandleResult(await products.ChangeStatusAsync(id, dto, cancellationToken));

    [Authorize(Policy = Policies.RequireAdmin)]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken) =>
        HandleResult(await products.DeleteAsync(id, cancellationToken));

    // -------------------------------------------------------------------------
    // Variants
    // -------------------------------------------------------------------------

    [HttpPost("{productId:long}/variants")]
    public async Task<IActionResult> AddVariant(
        long productId, CreateProductVariantDto dto, CancellationToken cancellationToken) =>
        HandleResult(await products.AddVariantAsync(productId, dto, cancellationToken));

    /// <summary>
    /// Variants are addressed by their own id, not nested under the product.
    /// </summary>
    /// <remarks>
    /// A variant id is globally unique, so <c>/variants/{id}</c> needs no
    /// product to resolve. Nesting it would invite a route where the product id
    /// and the variant disagree, and then a decision about which one wins.
    /// </remarks>
    [HttpPut("variants/{variantId:long}")]
    public async Task<IActionResult> UpdateVariant(
        long variantId, UpdateProductVariantDto dto, CancellationToken cancellationToken) =>
        HandleResult(await products.UpdateVariantAsync(variantId, dto, cancellationToken));

    [HttpDelete("variants/{variantId:long}")]
    public async Task<IActionResult> DeleteVariant(
        long variantId, CancellationToken cancellationToken) =>
        HandleResult(await products.DeleteVariantAsync(variantId, cancellationToken));
}
