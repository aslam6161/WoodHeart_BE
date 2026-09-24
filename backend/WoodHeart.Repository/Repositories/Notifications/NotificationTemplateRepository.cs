using Microsoft.EntityFrameworkCore;
using WoodHeart.Domain.Entity.Notifications;
using WoodHeart.Repository.Interfaces.Notifications;

namespace WoodHeart.Repository.Repositories.Notifications;

public class NotificationTemplateRepository(DataContext context)
    : Repository<NotificationTemplate>(context), INotificationTemplateRepository
{
    public async Task<NotificationTemplate?> GetByCodeAsync(
        string code, CancellationToken cancellationToken = default) =>
        await Set.FirstOrDefaultAsync(x => x.Code == code, cancellationToken);
}
