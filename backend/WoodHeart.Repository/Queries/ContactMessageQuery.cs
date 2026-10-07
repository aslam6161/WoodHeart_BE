using WoodHeart.Domain.Enums.Support;

namespace WoodHeart.Repository.Queries;

/// <summary>
/// Filters and paging for the shop's inbox.
/// </summary>
/// <remarks>
/// <para>
/// Lives here rather than beside the service DTOs for the same reason
/// <see cref="ProductQuery"/> does: the repository is what consumes it, and the
/// dependency rule runs Domain → Repository → Service.
/// </para>
/// <para>
/// <b>Spam is excluded unless it is asked for by name.</b> Junk that stays in
/// the default list is junk somebody has to scroll past every morning, and a
/// list people scroll past is a list they stop reading.
/// </para>
/// </remarks>
public class ContactMessageQuery : PaginationParams
{
    public ContactMessageStatus? Status { get; set; }

    public ContactTopic? Topic { get; set; }

    /// <summary>Matches the name, the message, the phone, the email or the reference.</summary>
    public string? Term { get; set; }
}
