using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoodHeart.Domain.Constants;
using WoodHeart.Service.DTOs.Support;
using WoodHeart.Service.Interfaces.Support;

namespace WoodHeart.Presentation.Controllers.Admin;

/// <summary>
/// The shop's inbox.
/// </summary>
/// <remarks>
/// All staff, reading and answering alike. Whoever picks up the telephone is
/// the person who should be able to see what was written and mark it dealt
/// with — putting that behind a manager is how an inbox becomes one person's
/// backlog.
/// </remarks>
[Authorize(Policy = Policies.RequireStaff)]
[Route("api/admin/contact-messages")]
public class AdminContactController(IContactAdminService contact) : BaseApiController
{
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] ContactMessageQueryDto query, CancellationToken cancellationToken) =>
        HandleResult(await contact.SearchAsync(query, cancellationToken));

    /// <summary>How many nobody has opened yet, for the badge in the menu.</summary>
    /// <remarks>Declared before <c>{id}</c>, or "new" is read as an id.</remarks>
    [HttpGet("new-count")]
    public async Task<IActionResult> NewCount(CancellationToken cancellationToken) =>
        HandleResult(await contact.CountNewAsync(cancellationToken));

    /// <summary>One message. Opening it marks it read.</summary>
    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken cancellationToken) =>
        HandleResult(await contact.GetAsync(id, cancellationToken));

    /// <summary>Moves it along, and keeps whatever staff wrote to each other about it.</summary>
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(
        long id,
        [FromBody] UpdateContactMessageDto dto,
        CancellationToken cancellationToken) =>
        HandleResult(await contact.UpdateAsync(id, dto, cancellationToken));
}
