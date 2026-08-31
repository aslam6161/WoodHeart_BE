using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoodHeart.Domain.Constants;
using WoodHeart.Service.DTOs.Catalog;
using WoodHeart.Service.Interfaces.Catalog;

namespace WoodHeart.Presentation.Controllers.Admin;

/// <summary>Brand management for the admin panel.</summary>
[Authorize(Policy = Policies.RequireAdminOrManager)]
[Route("api/admin/brands")]
public class AdminBrandsController(IBrandService brands) : BaseApiController
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        HandleResult(await brands.GetAllAsync(includeInactive: true, cancellationToken));

    [HttpGet("{id:long}", Name = nameof(GetBrandById))]
    public async Task<IActionResult> GetBrandById(long id, CancellationToken cancellationToken) =>
        HandleResult(await brands.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(CreateBrandDto dto, CancellationToken cancellationToken)
    {
        var result = await brands.CreateAsync(dto, cancellationToken);

        return HandleCreated(result, nameof(GetBrandById), new { id = result.Id });
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(
        long id, UpdateBrandDto dto, CancellationToken cancellationToken) =>
        HandleResult(await brands.UpdateAsync(id, dto, cancellationToken));

    [Authorize(Policy = Policies.RequireAdmin)]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken) =>
        HandleResult(await brands.DeleteAsync(id, cancellationToken));
}
