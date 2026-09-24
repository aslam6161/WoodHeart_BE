using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Common;
using WoodHeart.Domain.Entity.Notifications;
using WoodHeart.Domain.Enums.Common;
using WoodHeart.Domain.Helpers;
using WoodHeart.Domain.Settings;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Common;
using WoodHeart.Repository.Interfaces.Notifications;
using WoodHeart.Service.DTOs.Notifications;
using WoodHeart.Service.DTOs.Ordering;
using WoodHeart.Service.Infrastructure.Notifications;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Notifications;

namespace WoodHeart.Service.Services.Notifications;

/// <inheritdoc cref="INotificationAdminService" />
/// <remarks>
/// <para>
/// <b>The preview is the feature.</b> Anyone can be shown a list of template
/// names with switches beside them; what nobody can do from that list is answer
/// "what does this actually say, and what does sending it cost". So every
/// template here is rendered through the same code the delivery worker uses,
/// from a sample payload shaped like the real one, and reported with its billed
/// part count in each language it has a form in. A message that is one part in
/// English and three in Bangla is a different decision from one that is one in
/// both, and that difference is invisible from anywhere else.
/// </para>
/// <para>
/// <b>An unknown template code is a not-found, not a blank row.</b> The list is
/// the intersection of what the catalogue describes and what the database has a
/// row for — a row for a message nothing renders would read as configured and
/// send nothing, and a message with no row at all is treated as on, because a
/// deployment that adds a template should not silence it until somebody
/// notices.
/// </para>
/// </remarks>
public class NotificationAdminService(
    INotificationTemplateRepository templates,
    IOutboxRepository outbox,
    IStoreSettingService settings,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork,
    IOptions<SmsSettings> smsOptions,
    IOptions<EmailSettings> emailOptions,
    ILogger<NotificationAdminService> logger) : INotificationAdminService
{
    private readonly SmsSettings _sms = smsOptions.Value;

    private readonly EmailSettings _email = emailOptions.Value;

    // -------------------------------------------------------------------------
    // Templates
    // -------------------------------------------------------------------------

    public async Task<GeneralResponse<NotificationTemplatesDto>> GetTemplatesAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await templates.GetAllAsync(cancellationToken);
        var shopPhone = await ShopPhoneAsync(cancellationToken);

        var byCode = rows.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);

        return GeneralResponse<NotificationTemplatesDto>.Success(
            new NotificationTemplatesDto
            {
                Templates =
                [
                    .. NotificationCatalog.All.Select(entry =>
                        ToDto(entry, byCode.GetValueOrDefault(entry.Code), shopPhone))
                ],
                SmsGatewayConfigured = _sms.IsConfigured,
                EmailConfigured = _email.IsConfigured,
                ShopPhoneConfigured = !string.IsNullOrWhiteSpace(shopPhone)
            });
    }

    public async Task<GeneralResponse<NotificationTemplateDetailDto>> GetTemplateAsync(
        string code, CancellationToken cancellationToken = default)
    {
        if (NotificationCatalog.Find(code) is not { } entry)
        {
            return TemplateNotFound();
        }

        var row = await templates.GetByCodeAsync(entry.Code, cancellationToken);
        var shopPhone = await ShopPhoneAsync(cancellationToken);

        return GeneralResponse<NotificationTemplateDetailDto>.Success(
            ToDetail(entry, row, shopPhone));
    }

    public async Task<GeneralResponse<NotificationTemplateDetailDto>> UpdateTemplateAsync(
        string code, UpdateNotificationTemplateDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (NotificationCatalog.Find(code) is not { } entry)
        {
            return TemplateNotFound();
        }

        var shopPhone = await ShopPhoneAsync(cancellationToken);

        return await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var row = await templates.GetByCodeAsync(entry.Code, ct);

            if (row is null)
            {
                // The seed writes a row per known template, but a template
                // added in a deployment reaches this screen before anybody has
                // restarted the process that seeds it. Creating it on first
                // edit is better than refusing an edit to something the screen
                // is already showing.
                row = new NotificationTemplate { Code = entry.Code };

                await templates.InsertAsync(row, ct);
            }

            row.SmsEnabled = dto.SmsEnabled;
            row.EmailEnabled = dto.EmailEnabled;

            templates.Update(row);
            await unitOfWork.SaveChangesAsync(ct);

            // Worth its own line. "We stopped getting order texts in October"
            // is a question somebody asks months later, and this is the only
            // record that anybody chose it.
            NotificationAdminLog.TemplateConfigured(
                logger, row.Code, row.SmsEnabled, row.EmailEnabled);

            return GeneralResponse<NotificationTemplateDetailDto>.Success(
                ToDetail(entry, row, shopPhone));
        }, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Messages
    // -------------------------------------------------------------------------

    public async Task<GeneralResponse<PagedResult<NotificationMessageDto>>> SearchMessagesAsync(
        NotificationMessageQueryDto query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = await outbox.SearchAsync(
            new OutboxSearch(
                query.Term,
                query.Status,
                string.IsNullOrWhiteSpace(query.Type) ? null : query.Type.Trim(),
                query.Page,
                query.PageSize),
            cancellationToken);

        return GeneralResponse<PagedResult<NotificationMessageDto>>.Success(
            new PagedResult<NotificationMessageDto>
            {
                Items = [.. page.Select(ToDto)],
                Total = page.TotalCount,
                Page = page.CurrentPage,
                PageSize = page.PageSize
            });
    }

    public async Task<GeneralResponse<NotificationMessageDto>> ResendAsync(
        long id, CancellationToken cancellationToken = default) =>
        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var message = await outbox.GetForResendAsync(id, ct);

            if (message is null)
            {
                return GeneralResponse<NotificationMessageDto>.Fail(
                    NotificationErrors.MessageNotFound, "We could not find that message.");
            }

            if (!CanResend(message.Status))
            {
                return GeneralResponse<NotificationMessageDto>.Fail(
                    NotificationErrors.NotResendable,
                    message.Status == OutboxStatus.Pending
                        ? "This one is still queued — it is already on its way."
                        : "A worker is sending this one right now. Try again in a minute.");
            }

            message.Status = OutboxStatus.Pending;

            // Back to zero, so a message that exhausted its retries gets a full
            // set again rather than one attempt and another failure.
            message.AttemptCount = 0;
            message.NextAttemptAt = clock.UtcNow;
            message.LastError = null;
            message.ProcessedAt = null;

            // A reminder was held until the day before an appointment that has
            // since passed. Somebody pressing "send again" means now.
            message.NotBefore = null;

            outbox.Update(message);
            await unitOfWork.SaveChangesAsync(ct);

            NotificationAdminLog.MessageResent(logger, message.Id, message.Type);

            return GeneralResponse<NotificationMessageDto>.Success(ToDto(message));
        }, cancellationToken);

    // -------------------------------------------------------------------------
    // Mapping
    // -------------------------------------------------------------------------

    private static bool CanResend(OutboxStatus status) =>
        status is OutboxStatus.Processed or OutboxStatus.Failed or OutboxStatus.Suppressed;

    private static NotificationMessageDto ToDto(OutboxMessage message) =>
        new()
        {
            Id = message.Id,
            Type = message.Type,
            TemplateName = NotificationCatalog.Find(message.Type)?.Name,
            Status = message.Status,
            Reference = NotificationCatalog.Reference(message.IdempotencyKey, message.Payload),
            Recipient = NotificationCatalog.MaskedRecipient(message.Payload),
            AttemptCount = message.AttemptCount,
            CreatedAt = message.CreatedAt,
            NotBefore = message.NotBefore,
            NextAttemptAt = message.NextAttemptAt,
            ProcessedAt = message.ProcessedAt,
            LastError = message.LastError,
            CorrelationId = message.CorrelationId,
            CanResend = CanResend(message.Status)
        };

    private static NotificationTemplateDto ToDto(
        NotificationCatalogEntry entry, NotificationTemplate? row, string shopPhone)
    {
        var (english, bangla) = Previews(entry, shopPhone);

        return new NotificationTemplateDto
        {
            Code = entry.Code,
            Name = entry.Name,
            WhenItFires = entry.WhenItFires,
            Audience = entry.Audience,

            // A template with no row is on. See the class remarks: a deployment
            // that adds one should not silence it until an admin finds the
            // screen.
            SmsEnabled = row?.SmsEnabled ?? true,
            EmailEnabled = row?.EmailEnabled ?? true,
            SmsParts = english?.SmsParts ?? 0,
            BanglaSmsParts = bangla?.SmsParts
        };
    }

    private NotificationTemplateDetailDto ToDetail(
        NotificationCatalogEntry entry, NotificationTemplate? row, string shopPhone)
    {
        var summary = ToDto(entry, row, shopPhone);
        var (english, bangla) = Previews(entry, shopPhone);

        return new NotificationTemplateDetailDto
        {
            Code = summary.Code,
            Name = summary.Name,
            WhenItFires = summary.WhenItFires,
            Audience = summary.Audience,
            SmsEnabled = summary.SmsEnabled,
            EmailEnabled = summary.EmailEnabled,
            SmsParts = summary.SmsParts,
            BanglaSmsParts = summary.BanglaSmsParts,
            Previews = [.. new[] { english, bangla }.Where(p => p is not null).Select(p => p!)],
            SmsGatewayConfigured = _sms.IsConfigured,
            EmailConfigured = _email.IsConfigured,
            ShopPhoneConfigured = !string.IsNullOrWhiteSpace(shopPhone)
        };
    }

    /// <summary>
    /// Renders one sample of this template in each language it has a form in.
    /// </summary>
    /// <remarks>
    /// Through <c>NotificationTemplates.Render</c> — the same call the delivery
    /// worker makes — so a preview cannot drift from the message. A sample that
    /// fails to render is reported as no preview rather than as an error: the
    /// template list is not the place to find out about a bad sample, and the
    /// test that renders every one of them is.
    /// </remarks>
    private static (NotificationPreviewDto? English, NotificationPreviewDto? Bangla) Previews(
        NotificationCatalogEntry entry, string shopPhone) =>
        (Preview(entry, GlobalConstants.DefaultLanguage, shopPhone),
            entry.SupportsBangla
                ? Preview(entry, GlobalConstants.BanglaLanguage, shopPhone)
                : null);

    private static NotificationPreviewDto? Preview(
        NotificationCatalogEntry entry, string language, string shopPhone)
    {
        var sample = NotificationCatalog.Sample(entry.Code, language);

        if (sample is null)
        {
            return null;
        }

        RenderedNotification? rendered;

        try
        {
            rendered = NotificationTemplates.Render(entry.Code, sample, shopPhone);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }

        if (rendered is not { } notification)
        {
            return null;
        }

        return new NotificationPreviewDto
        {
            Language = language,
            SmsText = notification.SmsText,
            SmsParts = SmsParts.Count(notification.SmsText),
            IsUnicode = notification.SmsText?.Any(c => c > 127) == true,
            EmailSubject = notification.EmailSubject,
            EmailHtml = notification.EmailHtml
        };
    }

    private async Task<string> ShopPhoneAsync(CancellationToken cancellationToken) =>
        await settings.GetStringAsync(SettingKeys.StorePhone, cancellationToken) ?? string.Empty;

    private static GeneralResponse<NotificationTemplateDetailDto> TemplateNotFound() =>
        GeneralResponse<NotificationTemplateDetailDto>.Fail(
            NotificationErrors.TemplateNotFound,
            "Nothing in the shop sends a message of that kind.");
}

/// <summary>Event ids 2500–2501: what the shop sends, and sending it again.</summary>
public static partial class NotificationAdminLog
{
    [LoggerMessage(
        EventId = 2500,
        Level = LogLevel.Information,
        Message = "Notification template {Code} configured: sms={SmsEnabled}, email={EmailEnabled}")]
    public static partial void TemplateConfigured(
        ILogger logger, string code, bool smsEnabled, bool emailEnabled);

    [LoggerMessage(
        EventId = 2501,
        Level = LogLevel.Information,
        Message = "Outbox message {MessageId} ({Type}) put back in the queue by an admin")]
    public static partial void MessageResent(ILogger logger, long messageId, string type);
}
