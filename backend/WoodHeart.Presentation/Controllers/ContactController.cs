using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WoodHeart.Domain.Constants;
using WoodHeart.Service.DTOs.Support;
using WoodHeart.Service.Interfaces.Support;

namespace WoodHeart.Presentation.Controllers;

/// <summary>
/// How to reach the shop, and writing to it.
/// </summary>
/// <remarks>
/// <para>
/// <c>[AllowAnonymous]</c> throughout, and it has to be: somebody who cannot
/// sign in is exactly the person who needs to write in.
/// </para>
/// <para>
/// Sending is on the sensitive rate-limiting bucket rather than the public one.
/// It writes a row on behalf of an unauthenticated stranger, which is the shape
/// of thing that gets found and filled with junk; reading the shop's telephone
/// number beside it is ordinary browsing.
/// </para>
/// </remarks>
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Public)]
[Route("api/contact")]
public class ContactController(IContactService contact) : BaseApiController
{
    /// <summary>The shop's phone, email, address and hours, as far as they are filled in.</summary>
    [HttpGet("details")]
    public async Task<IActionResult> Details(CancellationToken cancellationToken) =>
        HandleResult(await contact.GetDetailsAsync(cancellationToken));

    /// <summary>Puts a message in the shop's inbox.</summary>
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [HttpPost]
    public async Task<IActionResult> Send(
        [FromBody] SubmitContactMessageDto dto, CancellationToken cancellationToken) =>
        HandleResult(await contact.SubmitAsync(dto, cancellationToken));
}
