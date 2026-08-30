using WoodHeart.Domain.Entity.Common;
using WoodHeart.Domain.Helpers;
using WoodHeart.Repository.Interfaces.Common;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Notifications;

namespace WoodHeart.Service.Services.Notifications;

/// <inheritdoc />
public class NotificationQueue(
    IOutboxRepository outbox,
    IDateTimeProvider clock,
    ICorrelationContext correlation) : INotificationQueue
{
    public async Task EnqueueAsync(
        NotificationRequest request, CancellationToken cancellationToken = default)
    {
        // Deliberately no SaveChangesAsync. The row joins the caller's unit of
        // work so the notification and the business change it announces commit
        // together — see IUnitOfWork for why saving here would be a bug.
        if (request.IdempotencyKey is { } key
            && await outbox.ExistsByIdempotencyKeyAsync(key, cancellationToken))
        {
            return;
        }

        await outbox.InsertAsync(
            new OutboxMessage
            {
                Type = request.Type,
                Payload = request.Payload,
                IdempotencyKey = request.IdempotencyKey,
                NotBefore = request.NotBefore,
                NextAttemptAt = request.NotBefore ?? clock.UtcNow,
                CorrelationId = correlation.CorrelationId
            },
            cancellationToken);
    }
}
