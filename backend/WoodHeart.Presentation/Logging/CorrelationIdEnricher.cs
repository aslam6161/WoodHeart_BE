using Microsoft.AspNetCore.Http;
using Serilog.Core;
using Serilog.Events;
using WoodHeart.Domain.Constants;

namespace WoodHeart.Presentation.Logging;

/// <summary>
/// Stamps the correlation id onto every log event raised during a request.
/// </summary>
/// <remarks>
/// <see cref="CorrelationIdMiddleware"/> already pushes the id onto Serilog's
/// LogContext, which covers most cases. This enricher is the safety net for
/// events raised outside that scope — startup, and framework logs written
/// before the middleware runs.
/// </remarks>
public class CorrelationIdEnricher(IHttpContextAccessor accessor) : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (logEvent.Properties.ContainsKey("CorrelationId"))
        {
            return;
        }

        if (accessor.HttpContext?.Items[GlobalConstants.CorrelationIdHeader] is string correlationId)
        {
            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty("CorrelationId", correlationId));
        }
    }
}
