using WoodHeart.Repository;
using WoodHeart.Service.DTOs.Ordering;
using WoodHeart.Service.DTOs.Support;

namespace WoodHeart.Service.Interfaces.Support;

/// <summary>The contact page: how to reach the shop, and writing to it.</summary>
public interface IContactService
{
    /// <summary>
    /// The shop's own contact details, as far as they have been filled in.
    /// </summary>
    /// <remarks>
    /// Always a success, even when the shop has entered nothing. There is no
    /// failure here to report: a blank address is a shop that has not got round
    /// to it, and the page still has a form on it.
    /// </remarks>
    Task<GeneralResponse<ContactDetailsDto>> GetDetailsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Takes a message and puts it in the shop's inbox.</summary>
    Task<GeneralResponse<ContactReceiptDto>> SubmitAsync(
        SubmitContactMessageDto dto, CancellationToken cancellationToken = default);
}

/// <summary>The inbox, for whoever answers it.</summary>
public interface IContactAdminService
{
    Task<GeneralResponse<PagedResult<ContactMessageListItemDto>>> SearchAsync(
        ContactMessageQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>
    /// One message, which opening marks as read.
    /// </summary>
    /// <remarks>
    /// <b>Reading it is what changes it.</b> Asking staff to press a button to
    /// say they have read something they are looking at is a button that gets
    /// pressed on the wrong rows, and a "new" count nobody believes. Only
    /// <c>New</c> moves — a message already answered is not reopened by somebody
    /// checking what was said.
    /// </remarks>
    Task<GeneralResponse<ContactMessageDto>> GetAsync(
        long id, CancellationToken cancellationToken = default);

    Task<GeneralResponse<ContactMessageDto>> UpdateAsync(
        long id, UpdateContactMessageDto dto, CancellationToken cancellationToken = default);

    /// <summary>How many nobody has opened, for the badge in the admin menu.</summary>
    Task<GeneralResponse<int>> CountNewAsync(CancellationToken cancellationToken = default);
}
