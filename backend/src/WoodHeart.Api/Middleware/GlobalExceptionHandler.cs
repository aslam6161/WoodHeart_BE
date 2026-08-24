using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WoodHeart.Domain.Common;

namespace WoodHeart.Api.Middleware;

/// <summary>
/// Last line of defence. Converts anything that escapes a handler into a clean
/// RFC 9457 problem response.
/// </summary>
/// <remarks>
/// <para>
/// Reaching here almost always means a bug — expected business failures come
/// back as <see cref="Result"/> values and never throw. The two exceptions are
/// genuine infrastructure conditions worth translating properly:
/// <see cref="DbUpdateConcurrencyException"/> (two admins editing one order) and
/// <see cref="OperationCanceledException"/> (the customer closed the tab).
/// </para>
/// <para>
/// In production the response carries no exception detail — a stack trace tells
/// an attacker the framework versions, file paths and library internals. The
/// correlation id is returned instead, which is all a support conversation
/// actually needs.
/// </para>
/// </remarks>
public sealed class GlobalExceptionHandler(
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // The client went away. Not our problem, and not worth an error log.
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            ApiLog.RequestAborted(logger, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            return true;
        }

        var (status, title, detail) = Translate(exception);

        if (status >= StatusCodes.Status500InternalServerError)
        {
            ApiLog.Unhandled(logger, context.Request.Method, context.Request.Path, exception);
        }
        else
        {
            ApiLog.Handled(logger, context.Request.Method, context.Request.Path, exception.Message);
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = ErrorCodeFor(exception),
            Detail = environment.IsDevelopment() ? exception.ToString() : detail,
            Instance = context.Request.Path
        };

        problem.Extensions["correlationId"] = context.TraceIdentifier;

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(problem, cancellationToken);

        return true;
    }

    private static (int Status, string Title, string Detail) Translate(Exception exception) => exception switch
    {
        DbUpdateConcurrencyException => (
            StatusCodes.Status409Conflict,
            "This record changed while you were editing it",
            "Someone else updated this record. Reload and try again."),

        DomainException => (
            StatusCodes.Status500InternalServerError,
            "Request could not be completed",
            "Something went wrong on our side. Please try again."),

        UnauthorizedAccessException => (
            StatusCodes.Status403Forbidden,
            "Not permitted",
            "You do not have permission to do that."),

        TimeoutException => (
            StatusCodes.Status504GatewayTimeout,
            "The request timed out",
            "This took too long to complete. Please try again."),

        _ => (
            StatusCodes.Status500InternalServerError,
            "Something went wrong",
            "An unexpected error occurred. Quote the correlation id if you contact support.")
    };

    private static string ErrorCodeFor(Exception exception) => exception switch
    {
        DbUpdateConcurrencyException => "common.concurrency_conflict",
        DomainException domain => domain.Code,
        UnauthorizedAccessException => "common.forbidden",
        TimeoutException => "common.timeout",
        _ => "common.unhandled_error"
    };
}

internal static partial class ApiLog
{
    [LoggerMessage(EventId = 1200, Level = LogLevel.Error,
        Message = "Unhandled exception for {Method} {Path}")]
    public static partial void Unhandled(ILogger logger, string method, string path, Exception exception);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Warning,
        Message = "Handled exception for {Method} {Path}: {Reason}")]
    public static partial void Handled(ILogger logger, string method, string path, string reason);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Debug,
        Message = "Client aborted the request for {Path}")]
    public static partial void RequestAborted(ILogger logger, string path);
}
