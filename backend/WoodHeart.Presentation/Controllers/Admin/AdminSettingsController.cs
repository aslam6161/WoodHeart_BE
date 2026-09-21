using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoodHeart.Domain.Constants;
using WoodHeart.Service.DTOs.Common;
using WoodHeart.Service.Interfaces.Common;

namespace WoodHeart.Presentation.Controllers.Admin;

/// <summary>
/// The store settings screen: VAT, delivery defaults, the shop's own details.
/// </summary>
/// <remarks>
/// Admin only, and narrower than the rest of the panel on purpose. A manager
/// runs the day's orders; the VAT rate, the free-delivery threshold and the
/// name on the invoice are the owner's decisions, and a wrong one is applied
/// to the next order placed.
/// </remarks>
[Authorize(Policy = Policies.RequireAdmin)]
[Route("api/admin/settings")]
public class AdminSettingsController(ISettingsAdminService settings) : BaseApiController
{
    /// <summary>Every setting, in the order the screen shows them.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        HandleResult(await settings.GetAllAsync(cancellationToken));

    /// <summary>
    /// Saves the values sent — all of them or none — and answers with the
    /// table as it now stands.
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> Update(UpdateSettingsDto dto, CancellationToken cancellationToken) =>
        HandleResult(await settings.UpdateAsync(dto, cancellationToken));
}
