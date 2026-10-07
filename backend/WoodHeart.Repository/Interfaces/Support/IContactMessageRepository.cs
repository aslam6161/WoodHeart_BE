using WoodHeart.Domain.Entity.Support;
using WoodHeart.Repository.Queries;

namespace WoodHeart.Repository.Interfaces.Support;

/// <summary>What customers have written to the shop.</summary>
public interface IContactMessageRepository : IRepository<ContactMessage>
{
    /// <summary>One page of the inbox, newest first.</summary>
    Task<PagedList<ContactMessage>> SearchAsync(
        ContactMessageQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many nobody has opened yet.
    /// </summary>
    /// <remarks>
    /// Its own query rather than a count off the list: the badge is drawn on
    /// every admin page, and paging the whole inbox to learn one number is the
    /// kind of thing that is cheap on the day it is written and expensive a
    /// year later.
    /// </remarks>
    Task<int> CountNewAsync(CancellationToken cancellationToken = default);
}
