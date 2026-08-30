using Microsoft.AspNetCore.Mvc;
using WoodHeart.Repository;

namespace WoodHeart.Presentation.Controllers;

/// <summary>
/// The base every controller derives from. Supplies the route convention and
/// the one place a <see cref="GeneralResponse"/> becomes an HTTP status.
/// </summary>
/// <remarks>
/// Controllers contain no business logic: build a DTO, call a service, hand the
/// result to <see cref="HandleResult{T}"/>. If a controller is making a
/// decision, that decision belongs in a service.
/// </remarks>
[ApiController]
[Route("api/[controller]")]
public class BaseApiController : ControllerBase
{
    /// <summary>
    /// Maps a service result onto a status code.
    /// </summary>
    /// <remarks>
    /// The mapping reads <see cref="GeneralResponse.ErrorCode"/> rather than
    /// asking each controller to choose, so <c>catalog.product_not_found</c>
    /// produces a 404 from every endpoint that can return it instead of a 404
    /// from some and a 400 from others.
    /// </remarks>
    protected IActionResult HandleResult<T>(GeneralResponse<T> result) =>
        result.IsSuccess ? Ok(result) : StatusCode(StatusFor(result.ErrorCode), result);

    protected IActionResult HandleResult(GeneralResponse result) =>
        result.IsSuccess ? Ok(result) : StatusCode(StatusFor(result.ErrorCode), result);

    /// <summary>Returns 201 with a Location header, for endpoints that create something.</summary>
    protected IActionResult HandleCreated<T>(GeneralResponse<T> result, string routeName, object routeValues) =>
        result.IsSuccess
            ? CreatedAtRoute(routeName, routeValues, result)
            : StatusCode(StatusFor(result.ErrorCode), result);

    private static int StatusFor(string? errorCode) => errorCode switch
    {
        null => StatusCodes.Status400BadRequest,
        var code when code.EndsWith(".not_found", StringComparison.Ordinal)
            => StatusCodes.Status404NotFound,
        var code when code.EndsWith(".forbidden", StringComparison.Ordinal)
            => StatusCodes.Status403Forbidden,
        var code when code.EndsWith(".unauthorized", StringComparison.Ordinal)
            => StatusCodes.Status401Unauthorized,
        var code when code.EndsWith(".conflict", StringComparison.Ordinal)
                      || code.EndsWith("_taken", StringComparison.Ordinal)
            => StatusCodes.Status409Conflict,
        // An upstream gateway failing is not the caller's fault, and a 400 would
        // tell the Angular client to stop retrying when it should retry.
        var code when code.StartsWith("external.", StringComparison.Ordinal)
            => StatusCodes.Status502BadGateway,
        _ => StatusCodes.Status400BadRequest
    };
}
