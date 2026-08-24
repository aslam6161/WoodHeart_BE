using WoodHeart.Infrastructure.Services;

namespace WoodHeart.Api.Middleware;

/// <summary>
/// Assigns every request a correlation id and echoes it back on the response.
/// </summary>
/// <remarks>
/// <para>
/// When a customer messages at 11pm saying "I paid but my order says unpaid",
/// this id is what turns a two-hour log trawl into a single query. It flows
/// Angular → API → Serilog → Hangfire job → bKash call, and the Angular error
/// interceptor shows it in the toast so the customer can quote it.
/// </para>
/// <para>
/// An inbound id is honoured so the client can correlate a retry with its
/// original attempt; otherwise one is minted here.
/// </para>
/// </remarks>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[CorrelationContext.HeaderName].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.CreateVersion7().ToString("N");
        }

        context.Items[CorrelationContext.HeaderName] = correlationId;
        context.TraceIdentifier = correlationId;

        // Registered before the response starts, because headers cannot be
        // added once the first byte has gone out.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationContext.HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
