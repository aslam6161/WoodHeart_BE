using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using WoodHeart.Domain.Constants;
using WoodHeart.Service.Interfaces.Common;

namespace WoodHeart.Service.Infrastructure.Correlation;

/// <summary>
/// Supplies the id that ties an Angular request, the API logs, a Hangfire job
/// and an outbound bKash call into one traceable story.
/// </summary>
/// <remarks>
/// Falls back to the ambient <see cref="Activity"/> id so background work still
/// correlates, and finally to a fresh id — the property is never null, so no
/// log line is ever orphaned.
/// </remarks>
public class CorrelationContext(IHttpContextAccessor accessor) : ICorrelationContext
{
    public string CorrelationId =>
        accessor.HttpContext?.Items[GlobalConstants.CorrelationIdHeader] as string
        ?? accessor.HttpContext?.TraceIdentifier
        ?? Activity.Current?.Id
        ?? Guid.CreateVersion7().ToString("N");
}
