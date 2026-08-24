using Mediator;
using Microsoft.AspNetCore.Mvc;
using WoodHeart.Domain.Common;

namespace WoodHeart.Api.Common;

/// <summary>
/// Base for every controller. Translates HTTP into a command or query and a
/// <see cref="Result"/> back into an HTTP response.
/// </summary>
/// <remarks>
/// <para>
/// Controllers in this codebase are intentionally boring. If one grows an
/// <c>if</c> about pricing, stock or eligibility, that logic belongs in the
/// Application or Domain layer — the architecture tests will not catch it, but
/// a reviewer should.
/// </para>
/// </remarks>
[ApiController]
[Route(ApiRoutes.V1 + "/[controller]")]
[Produces("application/json")]
public abstract class ApiController(ISender sender) : ControllerBase
{
    protected ISender Sender { get; } = sender;

    /// <summary>200 with the value, or the mapped problem response.</summary>
    protected IActionResult Respond<T>(Result<T> result) =>
        result.IsSuccess ? Ok(result.Value) : Problem(result.Error);

    /// <summary>204 on success, or the mapped problem response.</summary>
    protected IActionResult Respond(Result result) =>
        result.IsSuccess ? NoContent() : Problem(result.Error);

    /// <summary>201 with a Location header pointing at the new resource.</summary>
    protected IActionResult RespondCreated<T>(Result<T> result, string actionName, object routeValues) =>
        result.IsSuccess
            ? CreatedAtAction(actionName, routeValues, result.Value)
            : Problem(result.Error);

    /// <summary>
    /// Maps a domain <see cref="Error"/> onto an RFC 9457 problem response.
    /// </summary>
    /// <remarks>
    /// The <c>type</c> carries our stable error code (<c>ordering.insufficient_stock</c>),
    /// so the Angular client can branch on a code rather than string-matching an
    /// English message that a translator will change next month.
    /// </remarks>
    protected IActionResult Problem(Error error)
    {
        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.External => StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status400BadRequest
        };

        // Field-level failures use ValidationProblemDetails so Angular's
        // reactive forms can bind each message to the control that caused it.
        if (error.Type == ErrorType.Validation && error.ValidationErrors is { Count: > 0 })
        {
            var problem = new ValidationProblemDetails(
                error.ValidationErrors.ToDictionary(kv => kv.Key, kv => kv.Value))
            {
                Status = status,
                Title = "One or more fields need your attention.",
                Type = error.Code,
                Detail = error.Description,
                Instance = HttpContext.Request.Path
            };

            problem.Extensions["correlationId"] = HttpContext.TraceIdentifier;

            return new ObjectResult(problem) { StatusCode = status };
        }

        var details = new ProblemDetails
        {
            Status = status,
            Title = TitleFor(error.Type),
            Type = error.Code,
            Detail = error.Description,
            Instance = HttpContext.Request.Path
        };

        details.Extensions["correlationId"] = HttpContext.TraceIdentifier;

        return new ObjectResult(details) { StatusCode = status };
    }

    private static string TitleFor(ErrorType type) => type switch
    {
        ErrorType.Validation => "Validation failed",
        ErrorType.NotFound => "Not found",
        ErrorType.Conflict => "Conflict",
        ErrorType.Unauthorized => "Authentication required",
        ErrorType.Forbidden => "Not permitted",
        ErrorType.External => "A downstream service failed",
        _ => "Request could not be completed"
    };
}
