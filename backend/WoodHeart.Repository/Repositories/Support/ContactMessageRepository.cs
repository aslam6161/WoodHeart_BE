using Microsoft.EntityFrameworkCore;
using WoodHeart.Domain.Entity.Support;
using WoodHeart.Domain.Enums.Support;
using WoodHeart.Repository.Interfaces.Support;
using WoodHeart.Repository.Queries;

namespace WoodHeart.Repository.Repositories.Support;

public class ContactMessageRepository(DataContext context)
    : Repository<ContactMessage>(context), IContactMessageRepository
{
    public async Task<PagedList<ContactMessage>> SearchAsync(
        ContactMessageQuery query, CancellationToken cancellationToken = default)
    {
        var messages = QueryNoTracking();

        if (query.Status is { } status)
        {
            messages = messages.Where(message => message.Status == status);
        }
        else
        {
            // Junk is in the table but out of the way. Asking for it by name is
            // the only way to see it, which is what makes the default list
            // worth opening every morning.
            messages = messages.Where(message => message.Status != ContactMessageStatus.Spam);
        }

        if (query.Topic is { } topic)
        {
            messages = messages.Where(message => message.Topic == topic);
        }

        var term = query.Term?.Trim();

        if (!string.IsNullOrEmpty(term))
        {
            var pattern = $"%{term}%";

            // Whoever is searching has one of these five and does not know
            // which field it was typed into — usually a phone number read off a
            // missed call, or an order number from an SMS.
            messages = messages.Where(message =>
                EF.Functions.ILike(message.Name, pattern)
                || EF.Functions.ILike(message.Message, pattern)
                || (message.Phone != null && EF.Functions.ILike(message.Phone, pattern))
                || (message.Email != null && EF.Functions.ILike(message.Email, pattern))
                || (message.Reference != null && EF.Functions.ILike(message.Reference, pattern)));
        }

        // Newest first, then by id: two messages written in the same second
        // would otherwise swap places between pages and hide one of themselves.
        messages = messages
            .OrderByDescending(message => message.CreatedAt)
            .ThenByDescending(message => message.Id);

        return await PagedList<ContactMessage>.CreateAsync(
            messages, query.PageNumber, query.PageSize, cancellationToken);
    }

    public async Task<int> CountNewAsync(CancellationToken cancellationToken = default) =>
        await QueryNoTracking()
            .CountAsync(message => message.Status == ContactMessageStatus.New, cancellationToken);
}
