using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Common;
using WoodHeart.Domain.Enums.Common;
using WoodHeart.Domain.Helpers;
using WoodHeart.Domain.Settings;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Repository.Interfaces.Common;
using WoodHeart.Repository.Interfaces.Notifications;
using WoodHeart.Service.Infrastructure.Notifications;
using WoodHeart.Service.Interfaces.Common;
using WoodHeart.Service.Interfaces.Notifications;

namespace WoodHeart.Service.Services.Notifications;

/// <summary>
/// Drains the outbox: renders each due message and sends it.
/// </summary>
/// <remarks>
/// <para>
/// The other half of the pattern <c>OutboxMessage</c> describes. Services write
/// a row inside their own transaction and never touch a gateway; this runs
/// afterwards, on its own, and is the only thing in the application that talks
/// to an SMS provider.
/// </para>
/// <para>
/// <b>Claim and deliver are separate transactions, and they have to be.</b>
/// The claim marks a batch <c>Processing</c> and commits, which releases the
/// <c>FOR UPDATE SKIP LOCKED</c> rows so a second worker can take the next
/// batch. Holding that lock across the gateway calls would turn one slow
/// provider into a stall for every worker. The cost of separating them is that
/// a worker killed mid-batch strands its rows in <c>Processing</c> — which is
/// what <see cref="IOutboxRepository.ReclaimStaleAsync"/> exists for, and why
/// it runs first.
/// </para>
/// <para>
/// <b>Delivery is at-least-once, never exactly-once.</b> A message sent to the
/// gateway a moment before the process dies is delivered and then retried. The
/// defence is upstream — the idempotency key on the row means the same event
/// only ever produces one message to retry, so the worst case is one duplicate
/// SMS rather than a loop of them.
/// </para>
/// <para>
/// <b>SMS is the channel that decides the outcome.</b> Email is best-effort: a
/// mail server that is down must not cause the confirmation SMS to be sent five
/// more times, so an email failure is logged and the message is still marked
/// delivered. Most customers here have no email address at all.
/// </para>
/// <para>
/// <b>A template switched off here is suppressed, not dropped.</b> The row
/// keeps its reason — "SMS is turned off for this template" — so a message that
/// was never sent is distinguishable from one that failed, and so an admin who
/// turns the template back on can find the messages that went missing while it
/// was off and send them again.
/// </para>
/// </remarks>
public class OutboxDispatcher(
    IOutboxRepository outbox,
    INotificationTemplateRepository templates,
    ISmsSender sms,
    IEmailSender email,
    IStoreSettingService settings,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork,
    IOptions<OutboxSettings> options,
    ILogger<OutboxDispatcher> logger) : IOutboxDispatcher
{
    private readonly OutboxSettings _settings = options.Value;

    /// <summary>
    /// How long a message may sit in <c>Processing</c> before it is assumed
    /// orphaned.
    /// </summary>
    /// <remarks>
    /// Comfortably longer than the slowest plausible batch, so a worker that is
    /// merely slow does not have its rows taken from under it and delivered
    /// twice.
    /// </remarks>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var reclaimed = await outbox.ReclaimStaleAsync(
            clock.UtcNow.Subtract(StaleAfter), cancellationToken);

        if (reclaimed > 0)
        {
            NotificationLog.Reclaimed(logger, reclaimed);
        }

        // Claimed and committed on its own, so the row lock is released before
        // the first gateway call.
        var batch = await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var claimed = await outbox.ClaimDueBatchAsync(
                    clock.UtcNow, _settings.BatchSize, ct);

                await unitOfWork.SaveChangesAsync(ct);

                return claimed;
            },
            cancellationToken);

        if (batch.Count == 0)
        {
            return;
        }

        var shopPhone = await settings.GetStringAsync(SettingKeys.StorePhone, cancellationToken)
                        ?? string.Empty;

        // Once per pass rather than once per message: eight rows, and the batch
        // is twenty-five.
        var switches = (await templates.GetAllAsync(cancellationToken))
            .ToDictionary(x => x.Code, x => (x.SmsEnabled, x.EmailEnabled), StringComparer.OrdinalIgnoreCase);

        int delivered = 0, retried = 0, failed = 0, suppressed = 0;

        foreach (var message in batch)
        {
            var outcome = await DeliverAsync(message, shopPhone, switches, cancellationToken);

            switch (outcome)
            {
                case Outcome.Delivered: delivered++; break;
                case Outcome.Retry: retried++; break;
                case Outcome.GivenUp: failed++; break;
                default: suppressed++; break;
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        NotificationLog.BatchFinished(logger, delivered, retried, failed, suppressed);
    }

    private enum Outcome { Delivered, Retry, GivenUp, Suppressed }

    private async Task<Outcome> DeliverAsync(
        OutboxMessage message,
        string shopPhone,
        Dictionary<string, (bool SmsEnabled, bool EmailEnabled)> switches,
        CancellationToken cancellationToken)
    {
        // A type with no row is on. A deployment that adds a template reaches
        // here before the process that seeds its row has restarted, and a
        // message silenced by that gap would be one nobody ever chose to
        // silence.
        var (smsEnabled, emailEnabled) = switches.TryGetValue(message.Type, out var configured)
            ? configured
            : (true, true);

        if (!smsEnabled && !emailEnabled)
        {
            // Cheaper than rendering first, and the reason is clearer for it:
            // this message did not fail, it was not wanted.
            return Suppress(message, "Both channels are turned off for this template.");
        }

        RenderedNotification? rendered;

        try
        {
            rendered = NotificationTemplates.Render(message.Type, message.Payload, shopPhone);
        }
        catch (System.Text.Json.JsonException ex)
        {
            // A payload that cannot be parsed will not start parsing on the
            // fourth attempt. Suppress it and say so, rather than spending five
            // passes on it.
            return Suppress(message, $"{NotificationErrors.NoTemplate}: {ex.Message}");
        }

        if (rendered is not { } notification)
        {
            return Suppress(message, $"No template for '{message.Type}'.");
        }

        // A status nobody wrote words for — ReadyToShip, say. Silence is the
        // right answer, and it is a success rather than a failure.
        if (!notification.HasSomethingToSay)
        {
            return Suppress(message, "Nothing to say for this event.");
        }

        var smsResult = smsEnabled
            ? await TrySmsAsync(notification, cancellationToken)
            : SmsOutcome.Disabled;

        // Best-effort, and deliberately not part of the outcome. A mail server
        // that is down must not cause the confirmation SMS to be sent again.
        if (emailEnabled)
        {
            await TryEmailAsync(notification, cancellationToken);
        }

        return smsResult switch
        {
            SmsOutcome.Sent => Complete(message),

            // The shop chose not to pay for this one by SMS. Email is on —
            // both being off was handled above — so an address makes this a
            // delivery and no address makes it the silence that was asked for.
            SmsOutcome.Disabled => notification.RecipientEmail is not null
                ? Complete(message)
                : Suppress(message, "SMS is turned off for this template."),

            // Nothing to send to at all. Retrying will not conjure a phone
            // number, and the email — if there was one — has already gone.
            SmsOutcome.NoRecipient => notification.RecipientEmail is not null
                ? Complete(message)
                : Suppress(message, NotificationErrors.NoRecipient),

            _ => Reschedule(message, "The SMS gateway did not accept the message.")
        };
    }

    private enum SmsOutcome { Sent, NoRecipient, Failed, Disabled }

    private async Task<SmsOutcome> TrySmsAsync(
        RenderedNotification notification, CancellationToken cancellationToken)
    {
        if (notification.SmsText is not { Length: > 0 } text)
        {
            return SmsOutcome.NoRecipient;
        }

        // Stored normalised at placement, so this should always parse. It is
        // checked anyway because a row written months ago by an older version
        // of the code is not something to take on trust.
        if (!PhoneNumber.TryParse(notification.RecipientPhone, out var phone) || phone is null)
        {
            return SmsOutcome.NoRecipient;
        }

        var result = await sms.SendAsync(phone, text, cancellationToken);

        if (result.IsSuccess && result.Data is { } receipt)
        {
            NotificationLog.SmsSent(logger, phone.Masked, receipt.Parts, receipt.ProviderMessageId);

            return SmsOutcome.Sent;
        }

        return SmsOutcome.Failed;
    }

    private async Task TryEmailAsync(
        RenderedNotification notification, CancellationToken cancellationToken)
    {
        if (notification.RecipientEmail is not { Length: > 0 } address
            || notification.EmailSubject is not { Length: > 0 } subject
            || notification.EmailHtml is not { Length: > 0 } html)
        {
            return;
        }

        var result = await email.SendAsync(
            new EmailMessage { To = address, Subject = subject, HtmlBody = html },
            cancellationToken);

        if (result.IsSuccess)
        {
            NotificationLog.EmailSent(logger, address, subject);
        }
    }

    // -------------------------------------------------------------------------
    // Outcomes
    // -------------------------------------------------------------------------

    private Outcome Complete(OutboxMessage message)
    {
        message.Status = OutboxStatus.Processed;
        message.ProcessedAt = clock.UtcNow;
        message.LastError = null;
        message.NextAttemptAt = null;

        outbox.Update(message);

        return Outcome.Delivered;
    }

    private Outcome Suppress(OutboxMessage message, string reason)
    {
        message.Status = OutboxStatus.Suppressed;
        message.ProcessedAt = clock.UtcNow;
        message.LastError = reason;
        message.NextAttemptAt = null;

        outbox.Update(message);

        NotificationLog.Suppressed(logger, message.Id, message.Type, reason);

        return Outcome.Suppressed;
    }

    /// <summary>
    /// Puts the message back with an exponential backoff, or gives up.
    /// </summary>
    /// <remarks>
    /// The cap matters as much as the doubling. Uncapped, attempt ten would be
    /// eight hours out and the customer would hear about their order the
    /// following afternoon; capped, a message that is going to arrive arrives
    /// within the hour and one that is not reaches a person the same morning.
    /// </remarks>
    private Outcome Reschedule(OutboxMessage message, string error)
    {
        message.LastError = error;

        if (message.AttemptCount >= _settings.MaxAttempts)
        {
            message.Status = OutboxStatus.Failed;
            message.NextAttemptAt = null;

            outbox.Update(message);

            NotificationLog.GivenUp(logger, message.Id, message.Type, message.AttemptCount, error);

            return Outcome.GivenUp;
        }

        var delay = Math.Min(
            _settings.RetryBaseSeconds * Math.Pow(2, message.AttemptCount - 1),
            _settings.RetryMaxSeconds);

        message.Status = OutboxStatus.Pending;
        message.NextAttemptAt = clock.UtcNow.AddSeconds(delay);

        outbox.Update(message);

        NotificationLog.WillRetry(
            logger, message.Id, message.Type, message.AttemptCount, message.NextAttemptAt.Value, error);

        return Outcome.Retry;
    }
}
