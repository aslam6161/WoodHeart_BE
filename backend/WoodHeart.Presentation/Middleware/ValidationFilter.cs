using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WoodHeart.Repository;

namespace WoodHeart.Presentation.Middleware;

/// <summary>
/// Turns a failed model binding into the same <see cref="GeneralResponse"/>
/// shape every other failure uses.
/// </summary>
/// <remarks>
/// <para>
/// Without this, validation failures come back as ASP.NET's
/// <c>ValidationProblemDetails</c> while business failures come back as
/// <c>GeneralResponse</c> — two different error shapes from the same endpoint,
/// and an Angular error interceptor that has to guess which it received.
/// </para>
/// <para>
/// Field names are camelCased to match the Angular reactive form controls, so
/// the client can map <c>errors.phoneNumber</c> straight onto the control
/// without a translation table.
/// </para>
/// </remarks>
public class ValidationFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.IsValid)
        {
            return;
        }

        var errors = context.ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => ToCamelCase(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray());

        context.Result = new BadRequestObjectResult(
            GeneralResponse.Invalid("common.validation_failed", "Please correct the highlighted fields.", errors));
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
        // Nothing to do after the action runs.
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name) || char.IsLower(name[0]))
        {
            return name;
        }

        // Handles nested paths like "Address.PostCode" -> "address.postCode".
        return string.Join('.', name.Split('.').Select(LowerFirst));

        static string LowerFirst(string segment) =>
            string.IsNullOrEmpty(segment) || char.IsLower(segment[0])
                ? segment
                : char.ToLowerInvariant(segment[0]) + segment[1..];
    }
}
