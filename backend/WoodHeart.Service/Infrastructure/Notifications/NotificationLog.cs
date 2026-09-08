using Microsoft.Extensions.Logging;

namespace WoodHeart.Service.Infrastructure.Notifications;

/// <summary>
/// Source-generated logging for notification delivery, in the 1700 block.
/// </summary>
/// <remarks>
/// <para>
/// <b>Recipients are always masked and bodies are never logged in full,</b>
/// except by the logging sender, whose entire purpose is to show what would
/// have been sent on a machine with no gateway. An SMS body carries an order
/// number and a total; an email body carries a delivery address.
/// </para>
/// <para>
/// Nothing here logs a credential. The gateway key and the SMTP password are
/// spending credentials, and a log file is copied around far more casually than
/// a secret store.
/// </para>
/// </remarks>
internal static partial class NotificationLog
{
    [LoggerMessage(
        EventId = 1700,
        Level = LogLevel.Information,
        Message = "Outbox: {Delivered} delivered, {Retried} to retry, {Failed} given up, {Suppressed} suppressed.")]
    public static partial void BatchFinished(
        ILogger logger, int delivered, int retried, int failed, int suppressed);

    [LoggerMessage(
        EventId = 1701,
        Level = LogLevel.Information,
        Message = "SMS to {Recipient} sent, {Parts} part(s), gateway id {ProviderMessageId}.")]
    public static partial void SmsSent(
        ILogger logger, string recipient, int parts, string providerMessageId);

    /// <summary>
    /// The logging sender's output — the whole message, on purpose.
    /// </summary>
    /// <remarks>
    /// This is the one place a body is written out, and it only happens when no
    /// gateway is configured. It is what makes a developer machine able to read
    /// what a customer would have received.
    /// </remarks>
    [LoggerMessage(
        EventId = 1702,
        Level = LogLevel.Information,
        Message = "SMS NOT SENT (no gateway configured) to {Recipient}, {Parts} part(s): {Body}")]
    public static partial void SmsNotSent(ILogger logger, string recipient, int parts, string body);

    [LoggerMessage(
        EventId = 1703,
        Level = LogLevel.Warning,
        Message = "SMS to {Recipient} refused by the gateway ({StatusCode}): {Reply}")]
    public static partial void SmsGatewayRefused(
        ILogger logger, string recipient, int statusCode, string reply);

    [LoggerMessage(
        EventId = 1704,
        Level = LogLevel.Warning,
        Message = "SMS gateway unreachable for {Recipient}.")]
    public static partial void SmsGatewayUnreachable(ILogger logger, Exception exception, string recipient);

    [LoggerMessage(
        EventId = 1705,
        Level = LogLevel.Information,
        Message = "Email to {Recipient} sent: {Subject}")]
    public static partial void EmailSent(ILogger logger, string recipient, string subject);

    [LoggerMessage(
        EventId = 1706,
        Level = LogLevel.Information,
        Message = "Email NOT SENT (no mail server configured) to {Recipient}: {Subject}")]
    public static partial void EmailNotSent(ILogger logger, string recipient, string subject);

    [LoggerMessage(
        EventId = 1707,
        Level = LogLevel.Warning,
        Message = "Email to {Recipient} refused ({StatusCode}).")]
    public static partial void EmailRefused(
        ILogger logger, Exception exception, string recipient, string statusCode);

    [LoggerMessage(
        EventId = 1708,
        Level = LogLevel.Warning,
        Message = "Outbox message {MessageId} ({Type}) failed on attempt {Attempt}, retrying at {NextAttemptAt}: {Error}")]
    public static partial void WillRetry(
        ILogger logger,
        long messageId,
        string type,
        int attempt,
        DateTimeOffset nextAttemptAt,
        string error);

    /// <summary>
    /// Retries exhausted.
    /// </summary>
    /// <remarks>
    /// Error rather than Warning, because this is a customer who was told
    /// nothing about an order they placed, and it needs a person rather than a
    /// dashboard nobody opens.
    /// </remarks>
    [LoggerMessage(
        EventId = 1709,
        Level = LogLevel.Error,
        Message = "Outbox message {MessageId} ({Type}) given up after {Attempt} attempts: {Error}")]
    public static partial void GivenUp(
        ILogger logger, long messageId, string type, int attempt, string error);

    [LoggerMessage(
        EventId = 1710,
        Level = LogLevel.Warning,
        Message = "Outbox message {MessageId} suppressed ({Type}): {Reason}")]
    public static partial void Suppressed(ILogger logger, long messageId, string type, string reason);

    /// <summary>
    /// Rows reclaimed after a worker died holding them.
    /// </summary>
    /// <remarks>
    /// Warning rather than Information: it is normal after a deploy and
    /// abnormal at any other time, and the difference is worth being able to
    /// see.
    /// </remarks>
    [LoggerMessage(
        EventId = 1711,
        Level = LogLevel.Warning,
        Message = "Reclaimed {Count} outbox message(s) left in Processing by a worker that stopped.")]
    public static partial void Reclaimed(ILogger logger, int count);
}
