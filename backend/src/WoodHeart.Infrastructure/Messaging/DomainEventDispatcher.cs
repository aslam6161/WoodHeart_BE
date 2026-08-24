using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WoodHeart.Application.Common.Abstractions;
using WoodHeart.Domain.Common;

namespace WoodHeart.Infrastructure.Messaging;

/// <summary>
/// Publishes domain events through Mediator after the transaction has committed.
/// </summary>
/// <remarks>
/// <para>
/// Each event is dispatched in its own DI scope. Sharing the originating scope
/// would hand handlers the same <c>DbContext</c> whose transaction has just
/// closed, and one handler's change tracking would leak into the next.
/// </para>
/// <para>
/// A failing handler is logged and swallowed rather than propagated. By this
/// point the order is committed and the customer has been told it succeeded —
/// throwing here cannot undo that, and letting the exception escape would turn
/// a successful checkout into an HTTP 500. Reliability for side effects comes
/// from the outbox, which retries; this path is for in-process reactions only.
/// </para>
/// </remarks>
public sealed class DomainEventDispatcher(
    IServiceScopeFactory scopeFactory,
    ILogger<DomainEventDispatcher> logger) : IDomainEventDispatcher
{
    public async Task DispatchAsync(
        IReadOnlyCollection<IDomainEvent> domainEvents,
        CancellationToken cancellationToken = default)
    {
        foreach (var domainEvent in domainEvents)
        {
            await using var scope = scopeFactory.CreateAsyncScope();

            var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();

            try
            {
                if (domainEvent is INotification notification)
                {
                    await publisher.Publish(notification, cancellationToken);
                }
                else
                {
                    DomainEventLog.NotPublishable(logger, domainEvent.GetType().Name);
                }
            }
            catch (Exception ex)
            {
                DomainEventLog.HandlerFailed(logger, domainEvent.GetType().Name, domainEvent.EventId, ex);
            }
        }
    }
}

internal static partial class DomainEventLog
{
    [LoggerMessage(EventId = 1100, Level = LogLevel.Error,
        Message = "Handler for domain event {EventType} ({EventId}) failed after commit. " +
                  "The business change stands; the side effect did not run.")]
    public static partial void HandlerFailed(ILogger logger, string eventType, Guid eventId, Exception exception);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Warning,
        Message = "Domain event {EventType} does not implement INotification and was not published. " +
                  "Declare it as `: DomainEvent, INotification` to have handlers run.")]
    public static partial void NotPublishable(ILogger logger, string eventType);
}
