using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Exceptions;
using WoodHeart.Presentation.Errors;

namespace WoodHeart.Presentation.Middleware;

/// <summary>
/// The last line of defence. Anything that reaches here is a bug or an
/// infrastructure fault, because expected business failures are returned as
/// <c>GeneralResponse</c> values and never thrown.
/// </summary>
public class ExceptionMiddleware(
    RequestDelegate next,
    ILogger<ExceptionMiddleware> logger,
    IHostEnvironment environment)
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            await HandleAsync(context, exception);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        var correlationId = context.Items[GlobalConstants.CorrelationIdHeader] as string;

        var (status, message) = exception switch
        {
            // Two admins edited the same row, or two checkouts raced for the
            // last unit. Recoverable: the client should reload and retry.
            DbUpdateConcurrencyException =>
                (StatusCodes.Status409Conflict,
                    "This record changed while you were editing it. Reload and try again."),

            // An invariant was broken, which means a caller skipped a check it
            // should have made. A bug, so it is a 500 and it is logged loudly.
            DomainException =>
                (StatusCodes.Status500InternalServerError, "Something went wrong. Please try again."),

            UnauthorizedAccessException =>
                (StatusCodes.Status403Forbidden, "You do not have permission to do that."),

            // 499 is nginx's "client closed request". Not in StatusCodes, but it
            // keeps a browser navigating away out of the 5xx alerting graphs.
            OperationCanceledException => (499, "The request was cancelled."),

            TimeoutException =>
                (StatusCodes.Status504GatewayTimeout, "The request took too long. Please try again."),

            _ => (StatusCodes.Status500InternalServerError, "Something went wrong. Please try again.")
        };

        // A cancelled request is the browser navigating away, not a fault.
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            ApiLog.RequestCancelled(logger, context.Request.Path);
        }
        else
        {
            ApiLog.Unhandled(
                logger,
                exception.GetType().Name,
                context.Request.Method,
                context.Request.Path,
                correlationId,
                exception);
        }

        if (context.Response.HasStarted)
        {
            // Too late to change the status code; the log above is all we can do.
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";

        var body = new ApiException(
            status,
            message,
            correlationId,
            environment.IsDevelopment() ? exception.ToString() : null);

        await context.Response.WriteAsync(JsonSerializer.Serialize(body, SerializerOptions));
    }
}

/// <summary>
/// Source-generated log methods.
/// </summary>
/// <remarks>
/// This runs on every request that faults, and the plain
/// <c>logger.LogError(...)</c> form boxes each argument and formats the message
/// even when the level is disabled. The generator emits code that does neither.
/// </remarks>
internal static partial class ApiLog
{
    [LoggerMessage(
        EventId = 1200,
        Level = LogLevel.Information,
        Message = "Request {Path} was cancelled by the client.")]
    public static partial void RequestCancelled(ILogger logger, PathString path);

    [LoggerMessage(
        EventId = 1201,
        Level = LogLevel.Error,
        Message = "Unhandled {ExceptionType} on {Method} {Path}. CorrelationId {CorrelationId}.")]
    public static partial void Unhandled(
        ILogger logger,
        string exceptionType,
        string method,
        PathString path,
        string? correlationId,
        Exception exception);
}
