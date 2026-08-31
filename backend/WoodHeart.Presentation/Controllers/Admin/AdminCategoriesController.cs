using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoodHeart.Domain.Constants;
using WoodHeart.Service.DTOs.Catalog;
using WoodHeart.Service.Interfaces.Catalog;

namespace WoodHeart.Presentation.Controllers.Admin;

/// <summary>
/// Category management for the admin panel.
/// </summary>
/// <remarks>
/// No business logic here — build a DTO, call the service, hand the result to
/// <c>HandleResult</c>. Every decision, including which failures are 404 versus
/// 409, is made elsewhere: the service returns an <c>ErrorCode</c> and the base
/// controller maps it.
/// </remarks>
[Authorize(Policy = Policies.RequireAdminOrManager)]
[Route("api/admin/categories")]
public class AdminCategoriesController(ICategoryService categories) : BaseApiController
{
    /// <summary>The full tree, including inactive categories.</summary>
    [HttpGet]
    public async Task<IActionResult> GetTree(CancellationToken cancellationToken) =>
        HandleResult(await categories.GetTreeAsync(includeInactive: true, cancellationToken));

    [HttpGet("{id:long}", Name = nameof(GetCategoryById))]
    public async Task<IActionResult> GetCategoryById(long id, CancellationToken cancellationToken) =>
        HandleResult(await categories.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(
        CreateCategoryDto dto, CancellationToken cancellationToken)
    {
        var result = await categories.CreateAsync(dto, cancellationToken);

        return HandleCreated(result, nameof(GetCategoryById), new { id = result.Id });
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(
        long id, UpdateCategoryDto dto, CancellationToken cancellationToken) =>
        HandleResult(await categories.UpdateAsync(id, dto, cancellationToken));

    /// <summary>
    /// Re-parents a category and everything beneath it.
    /// </summary>
    /// <remarks>
    /// Its own endpoint rather than a field on the update, because it rewrites
    /// the materialized path of every descendant and has to be checked for
    /// cycles. Folding it into <c>PUT</c> would make every rename pay that cost
    /// and let any caller trigger it by accident.
    /// </remarks>
    [HttpPost("{id:long}/move")]
    public async Task<IActionResult> Move(
        long id, MoveCategoryDto dto, CancellationToken cancellationToken) =>
        HandleResult(await categories.MoveAsync(id, dto, cancellationToken));

    /// <summary>Soft-deletes a leaf category. Refuses while it has children or products.</summary>
    [Authorize(Policy = Policies.RequireAdmin)]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken) =>
        HandleResult(await categories.DeleteAsync(id, cancellationToken));
}
