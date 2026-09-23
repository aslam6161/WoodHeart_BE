using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoodHeart.Domain.Constants;
using WoodHeart.Service.DTOs.Payments;
using WoodHeart.Service.Interfaces.Payments;

namespace WoodHeart.Presentation.Controllers.Admin;

/// <summary>
/// How the shop takes money: which methods, where, with what charge, and
/// against whose merchant account.
/// </summary>
/// <remarks>
/// <para>
/// Admin only, and narrower than the rest of the panel for the same reason the
/// settings screen is. A manager runs the day's orders; whether cash on
/// delivery stops at 100,000৳, and which merchant account bKash points at, are
/// the owner's decisions — and this route holds credentials.
/// </para>
/// <para>
/// <b>No create, no delete.</b> A method's code is the join to the class that
/// implements it, so a row invented here would read as configured and never
/// appear at checkout. Methods arrive with the code that serves them.
/// </para>
/// </remarks>
[Authorize(Policy = Policies.RequireAdmin)]
[Route("api/admin/payment-methods")]
public class AdminPaymentMethodsController(IPaymentMethodAdminService methods) : BaseApiController
{
    /// <summary>Every method, enabled or not, in the shop's display order.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        HandleResult(await methods.GetAllAsync(cancellationToken));

    [HttpGet("{code}")]
    public async Task<IActionResult> Get(string code, CancellationToken cancellationToken) =>
        HandleResult(await methods.GetAsync(code, cancellationToken));

    /// <summary>
    /// Saves one method whole.
    /// </summary>
    /// <remarks>
    /// The credential field is write-only in both directions: absent leaves
    /// what is stored alone, an empty string clears it, anything else replaces
    /// it. Nothing here ever answers with one.
    /// </remarks>
    [HttpPut("{code}")]
    public async Task<IActionResult> Update(
        string code, UpdatePaymentMethodDto dto, CancellationToken cancellationToken) =>
        HandleResult(await methods.UpdateAsync(code, dto, cancellationToken));
}
