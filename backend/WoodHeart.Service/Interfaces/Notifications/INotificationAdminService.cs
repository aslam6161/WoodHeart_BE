using WoodHeart.Repository;
using WoodHeart.Service.DTOs.Notifications;
using WoodHeart.Service.DTOs.Ordering;

namespace WoodHeart.Service.Interfaces.Notifications;

/// <summary>
/// What the shop sends, whether it is sending it, and what became of each one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two halves of one question.</b> The templates decide what goes out; the
/// messages say what happened when it did. A toggle with no record beside it is
/// a switch with no feedback — an admin who turns the quotation SMS off has no
/// way to see the silence, and one whose gateway has been refusing every
/// message for a week has no way to find out at all. The outbox has always
/// recorded both; until now nothing read it back.
/// </para>
/// <para>
/// <b>There is no create and no delete.</b> A template's code is the join to
/// the method in <c>NotificationTemplates</c> that renders it, exactly as a
/// payment method's code is the join to its provider. A row invented from a
/// screen would read as configured and produce nothing.
/// </para>
/// </remarks>
public interface INotificationAdminService
{
    /// <summary>Every kind of message, with what each costs to send.</summary>
    Task<GeneralResponse<NotificationTemplatesDto>> GetTemplatesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>One template, rendered from a sample in every language it has.</summary>
    Task<GeneralResponse<NotificationTemplateDetailDto>> GetTemplateAsync(
        string code, CancellationToken cancellationToken = default);

    Task<GeneralResponse<NotificationTemplateDetailDto>> UpdateTemplateAsync(
        string code,
        UpdateNotificationTemplateDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>The outbox, newest first.</summary>
    Task<GeneralResponse<PagedResult<NotificationMessageDto>>> SearchMessagesAsync(
        NotificationMessageQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts one message back in the queue.
    /// </summary>
    /// <remarks>
    /// The attempt count goes back to zero and any hold is lifted, so a
    /// reminder that failed is delivered now rather than waiting again for a
    /// time that has passed. Refused for a message already queued or one a
    /// worker is currently holding.
    /// </remarks>
    Task<GeneralResponse<NotificationMessageDto>> ResendAsync(
        long id, CancellationToken cancellationToken = default);
}
