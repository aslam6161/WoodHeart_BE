using Serilog.Context;
using WoodHeart.Domain.Constants;

namespace WoodHeart.Presentation.Middleware;

/// <summary>
/// Assigns every request a correlation id, echoes it back, and pushes it into
/// the Serilog context so every log line for the request carries it.
/// </summary>
/// <remarks>
/// Honours an inbound id when the Angular client supplies one. That is what
/// makes a customer's "my order failed at 4pm" traceable from the browser
/// through the API to the bKash call and back.
/// </remarks>
public class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);

        context.Items[GlobalConstants.CorrelationIdHeader] = correlationId;

        // Registered on the response object rather than set directly, because by
        // the time the pipeline unwinds the response may already have started.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[GlobalConstants.CorrelationIdHeader] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        var inbound = context.Request.Headers[GlobalConstants.CorrelationIdHeader].FirstOrDefault();

        // Capped: the value is echoed into every log line, and an unbounded
        // client-supplied string is a cheap way to bloat the logs.
        return !string.IsNullOrWhiteSpace(inbound) && inbound.Length <= 64
            ? inbound
            : Guid.CreateVersion7().ToString("N");
    }
}
