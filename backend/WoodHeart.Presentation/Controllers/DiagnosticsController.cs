using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WoodHeart.Domain.Constants;
using WoodHeart.Service.DTOs.Common;
using WoodHeart.Service.Interfaces.Common;

namespace WoodHeart.Presentation.Controllers;

/// <summary>
/// The walking skeleton. Exercises routing, model binding, validation, the
/// service layer, the clock and the error contract — end to end, in production,
/// without needing a database.
/// </summary>
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Public)]
public class DiagnosticsController(IDiagnosticsService service) : BaseApiController
{
    [HttpGet("ping", Name = "Ping")]
    public ActionResult<PingResponseDto> Ping() => Ok(service.Ping());

    [HttpPost("echo", Name = "Echo")]
    public IActionResult Echo(EchoRequestDto request) => HandleResult(service.Echo(request));
}
