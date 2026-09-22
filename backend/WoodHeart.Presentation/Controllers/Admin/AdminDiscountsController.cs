using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Enums.Promotions;
using WoodHeart.Service.DTOs.Promotions;
using WoodHeart.Service.Interfaces.Promotions;

namespace WoodHeart.Presentation.Controllers.Admin;

/// <summary>
/// Discounts: automatic promotions and coupon codes.
/// </summary>
/// <remarks>
/// <para>
/// Reading is for all staff — somebody answering the phone needs to be able to
/// say whether a code is still running. Writing is for an admin or a manager,
/// because every row here is money the shop has decided to give away, and a
/// misplaced decimal point on a percentage is a very expensive typo.
/// </para>
/// <para>
/// There is no delete. A discount that has been given to an order is part of
/// that order's history; <c>Archived</c> is how one goes away.
/// </para>
/// </remarks>
[Authorize(Policy = Policies.RequireStaff)]
[Route("api/admin/discounts")]
public class AdminDiscountsController(IDiscountAdminService discounts) : BaseApiController
{
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] DiscountQueryDto query, CancellationToken cancellationToken) =>
        HandleResult(await discounts.SearchAsync(query, cancellationToken));

    [HttpGet("{id:long}", Name = nameof(GetDiscount))]
    public async Task<IActionResult> GetDiscount(long id, CancellationToken cancellationToken) =>
        HandleResult(await discounts.GetAsync(id, cancellationToken));

    [Authorize(Policy = Policies.RequireAdminOrManager)]
    [HttpPost]
    public async Task<IActionResult> Create(
        SaveDiscountDto dto, CancellationToken cancellationToken)
    {
        var result = await discounts.CreateAsync(dto, cancellationToken);

        return HandleCreated(result, nameof(GetDiscount), new { id = result.Id });
    }

    [Authorize(Policy = Policies.RequireAdminOrManager)]
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(
        long id, SaveDiscountDto dto, CancellationToken cancellationToken) =>
        HandleResult(await discounts.UpdateAsync(id, dto, cancellationToken));

    /// <summary>Switches a discount on, off, or away, without touching the rest of it.</summary>
    [Authorize(Policy = Policies.RequireAdminOrManager)]
    [HttpPut("{id:long}/status")]
    public async Task<IActionResult> SetStatus(
        long id, SetDiscountStatusDto dto, CancellationToken cancellationToken) =>
        HandleResult(await discounts.SetStatusAsync(id, dto.Status, cancellationToken));

    /// <summary>Who used it, on which order, and for how much.</summary>
    [HttpGet("{id:long}/usage")]
    public async Task<IActionResult> Usage(
        long id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        HandleResult(await discounts.GetUsageAsync(id, page, pageSize, cancellationToken));
}

/// <summary>The one field the status endpoint takes.</summary>
/// <remarks>
/// A body rather than a path segment, so that <c>Archived</c> cannot be reached
/// by guessing at a URL, and so the shape is the same as every other write here.
/// </remarks>
public class SetDiscountStatusDto
{
    public DiscountStatus Status { get; init; }
}
