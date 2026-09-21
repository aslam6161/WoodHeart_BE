using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoodHeart.Domain.Constants;
using WoodHeart.Service.DTOs.Inventory;
using WoodHeart.Service.Interfaces.Inventory;

namespace WoodHeart.Presentation.Controllers.Admin;

/// <summary>
/// Stock: what is on the shelf, what happened to it, and stock-ins by hand.
/// </summary>
/// <remarks>
/// Reading is for all staff — the person packing an order needs to know
/// whether the bed is there. Writing a movement is for whoever is
/// accountable for the count: a stock-in that did not happen or a write-off
/// that did are both money.
/// </remarks>
[Authorize(Policy = Policies.RequireStaff)]
[Route("api/admin/stock")]
public class AdminStockController(IInventoryService inventory) : BaseApiController
{
    /// <summary>The stock list: every stocked variant, its count, and whether it is low.</summary>
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] StockQueryDto query, CancellationToken cancellationToken) =>
        HandleResult(await inventory.SearchAsync(query, cancellationToken));

    /// <summary>The three numbers at the top of the list.</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken) =>
        HandleResult(await inventory.SummaryAsync(cancellationToken));

    [HttpGet("{variantId:long}")]
    public async Task<IActionResult> Level(long variantId, CancellationToken cancellationToken) =>
        HandleResult(await inventory.GetLevelAsync(variantId, cancellationToken));

    /// <summary>The ledger for one variant, newest first.</summary>
    [HttpGet("{variantId:long}/movements")]
    public async Task<IActionResult> Movements(
        long variantId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        HandleResult(await inventory.GetMovementsAsync(variantId, page, pageSize, cancellationToken));

    /// <summary>A stock-in, a write-off, a correction or a transfer.</summary>
    [Authorize(Policy = Policies.RequireAdminOrManager)]
    [HttpPost("{variantId:long}/movements")]
    public async Task<IActionResult> Adjust(
        long variantId, AdjustStockDto dto, CancellationToken cancellationToken) =>
        HandleResult(await inventory.AdjustAsync(variantId, dto, cancellationToken));
}
