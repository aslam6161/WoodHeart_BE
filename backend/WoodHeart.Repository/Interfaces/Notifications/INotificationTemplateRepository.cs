using WoodHeart.Domain.Entity.Notifications;

namespace WoodHeart.Repository.Interfaces.Notifications;

/// <summary>Which messages the shop is sending, and by which channel.</summary>
/// <remarks>
/// The inherited <c>GetAllAsync</c> is the one the delivery worker calls, and
/// the whole table in one query is the right shape for it: there are eight
/// rows, and eight read once per batch is cheaper than eight lookups per
/// message.
/// </remarks>
public interface INotificationTemplateRepository : IRepository<NotificationTemplate>
{
    /// <remarks>Tracked: the admin screen that reads one by code is about to change it.</remarks>
    Task<NotificationTemplate?> GetByCodeAsync(
        string code, CancellationToken cancellationToken = default);
}
