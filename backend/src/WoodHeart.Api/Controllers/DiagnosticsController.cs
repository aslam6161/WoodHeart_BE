using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WoodHeart.Api.Common;
using WoodHeart.Api.Extensions;
using WoodHeart.Application.Common.Abstractions;
using WoodHeart.Application.Diagnostics;

namespace WoodHeart.Api.Controllers;

/// <summary>
/// Proves the whole stack is wired: routing → pipeline → validation →
/// handler → Result → problem mapping.
/// </summary>
/// <remarks>
/// This is Phase 0's walking skeleton. It stays in the codebase permanently as
/// a deployment smoke test — if <c>/api/v1/diagnostics/ping</c> answers and
/// <c>/echo</c> validates, the plumbing is sound and any failure is in the
/// feature code, not the foundation.
/// </remarks>
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Public)]
public sealed class DiagnosticsController(ISender sender, IDateTimeProvider clock)
    : ApiController(sender)
{
    /// <summary>Liveness with a Dhaka timestamp — confirms the timezone wiring too.</summary>
    [HttpGet("ping")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Ping() => Ok(new
    {
        service = "WoodHeart.Api",
        status = "ok",
        utc = clock.UtcNow,
        dhaka = clock.DhakaNow,
        dhakaToday = clock.DhakaToday
    });

    /// <summary>
    /// Round-trips a message through the full Mediator pipeline.
    /// </summary>
    /// <remarks>
    /// Send an empty or over-long message to see the validation pipeline
    /// produce a 400 with a per-field <c>errors</c> dictionary — the exact shape
    /// the Angular reactive forms bind to.
    /// </remarks>
    [HttpPost("echo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Echo(
        [FromBody] EchoRequest request,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(
            new EchoQuery(request.Message, request.PhoneNumber),
            cancellationToken);

        return Respond(result);
    }
}

public sealed record EchoRequest(string Message, string? PhoneNumber);
