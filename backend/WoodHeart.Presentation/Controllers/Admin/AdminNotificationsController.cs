using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoodHeart.Domain.Constants;
using WoodHeart.Service.DTOs.Notifications;
using WoodHeart.Service.Interfaces.Notifications;

namespace WoodHeart.Presentation.Controllers.Admin;

/// <summary>
/// What the shop tells people, and what became of each message.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reading is for all staff; the switches are the owner's.</b> Whoever
/// answers the telephone is the person asked "I never got a text about my
/// order" and needs to be able to look, and to send it again. Whether the shop
/// pays for an SMS at all is a decision about money, which is why it sits
/// behind the same policy as the settings and payment screens.
/// </para>
/// <para>
/// <b>No create and no delete.</b> A template's code is the join to the method
/// that renders it, so a row invented here would read as configured and produce
/// nothing. Messages are a log; a log an admin can edit is not one.
/// </para>
/// </remarks>
[Authorize(Policy = Policies.RequireStaff)]
[Route("api/admin/notifications")]
public class AdminNotificationsController(INotificationAdminService notifications) : BaseApiController
{
    /// <summary>Every kind of message, with what each costs to send.</summary>
    [HttpGet("templates")]
    public async Task<IActionResult> GetTemplates(CancellationToken cancellationToken) =>
        HandleResult(await notifications.GetTemplatesAsync(cancellationToken));

    /// <summary>One template, with the message it would actually produce.</summary>
    [HttpGet("templates/{code}")]
    public async Task<IActionResult> GetTemplate(
        string code, CancellationToken cancellationToken) =>
        HandleResult(await notifications.GetTemplateAsync(code, cancellationToken));

    /// <summary>Turns one template's channels on or off. The wording is not editable.</summary>
    [Authorize(Policy = Policies.RequireAdmin)]
    [HttpPut("templates/{code}")]
    public async Task<IActionResult> UpdateTemplate(
        string code,
        UpdateNotificationTemplateDto dto,
        CancellationToken cancellationToken) =>
        HandleResult(await notifications.UpdateTemplateAsync(code, dto, cancellationToken));

    /// <summary>The outbox, newest first.</summary>
    [HttpGet("messages")]
    public async Task<IActionResult> SearchMessages(
        [FromQuery] NotificationMessageQueryDto query, CancellationToken cancellationToken) =>
        HandleResult(await notifications.SearchMessagesAsync(query, cancellationToken));

    /// <summary>
    /// Puts one message back in the queue.
    /// </summary>
    /// <remarks>
    /// Staff rather than admin: this is the answer to "I never got the text",
    /// and it is the same message to the same person rather than a new decision
    /// about what the shop sends.
    /// </remarks>
    [HttpPost("messages/{id:long}/resend")]
    public async Task<IActionResult> Resend(long id, CancellationToken cancellationToken) =>
        HandleResult(await notifications.ResendAsync(id, cancellationToken));
}
