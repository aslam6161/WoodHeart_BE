using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Settings;
using WoodHeart.Repository;
using WoodHeart.Service.Interfaces.Notifications;

namespace WoodHeart.Service.Infrastructure.Notifications;

/// <summary>
/// The sender used when no mail server is configured.
/// </summary>
/// <remarks>
/// Same reasoning as <see cref="LoggingSmsSender"/>: a fresh clone must work
/// without credentials, and a swallowed notification must still leave a trace.
/// The body is not logged — it is long, it is HTML, and it contains the
/// customer's address.
/// </remarks>
public class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task<GeneralResponse<string>> SendAsync(
        EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        NotificationLog.EmailNotSent(logger, message.To, message.Subject);

        return Task.FromResult(GeneralResponse<string>.Success($"logged-{Guid.NewGuid():n}"));
    }
}

/// <summary>
/// Ordinary SMTP.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SmtpClient"/> from the framework rather than a mail library.
/// Microsoft's guidance points elsewhere for new development, and the reason
/// is real — it has no modern OAuth support and its async is a wrapper over an
/// event-based API. Neither matters here: this sends a handful of
/// transactional messages a day through a host that takes a username and a
/// password, and the alternative is another dependency in the graph to audit
/// and keep patched for a feature that is the <i>secondary</i> channel in a
/// market where most customers have no email they read.
/// </para>
/// <para>
/// Revisit this if the shop ever needs Gmail/Microsoft OAuth or bulk sending.
/// Like the SMS gateway, it is one class.
/// </para>
/// <para>
/// <b>The password never appears in a log or an error message.</b> Failures
/// report the SMTP status code and the server's own text, and nothing else.
/// </para>
/// </remarks>
public class SmtpEmailSender(
    IOptions<EmailSettings> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly EmailSettings _settings = options.Value;

    public async Task<GeneralResponse<string>> SendAsync(
        EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var mail = new MailMessage
        {
            From = new MailAddress(_settings.FromAddress, _settings.FromName),
            Subject = message.Subject,
            Body = message.HtmlBody,
            IsBodyHtml = true
        };

        mail.To.Add(message.To);

        // A plain-text alternative where one was written. Some clients show it
        // instead, and a message with only an HTML part is more likely to be
        // filed as spam.
        if (!string.IsNullOrWhiteSpace(message.PlainTextBody))
        {
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                message.PlainTextBody, null, "text/plain"));

            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                message.HtmlBody, null, "text/html"));
        }

        foreach (var attachment in message.Attachments)
        {
            var stream = new MemoryStream(attachment.Content);
            mail.Attachments.Add(new Attachment(stream, attachment.FileName, attachment.ContentType));
        }

        using var client = new SmtpClient(_settings.Host, _settings.Port)
        {
            EnableSsl = _settings.UseStartTls,
            Timeout = _settings.TimeoutSeconds * 1000,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Credentials = string.IsNullOrWhiteSpace(_settings.Username)
                ? null
                : new NetworkCredential(_settings.Username, _settings.Password)
        };

        try
        {
            await client.SendMailAsync(mail, cancellationToken);

            return GeneralResponse<string>.Success(mail.Headers["Message-ID"] ?? "sent");
        }
        catch (SmtpException ex)
        {
            NotificationLog.EmailRefused(logger, ex, message.To, ex.StatusCode.ToString());

            return GeneralResponse<string>.Fail(
                NotificationErrors.EmailUnavailable, $"The mail server refused the message: {ex.StatusCode}.");
        }
        catch (InvalidOperationException ex)
        {
            // A misconfigured host or port — the client throws this rather than
            // an SmtpException when it cannot even start.
            NotificationLog.EmailRefused(logger, ex, message.To, "configuration");

            return GeneralResponse<string>.Fail(
                NotificationErrors.EmailUnavailable, "The mail server is not configured correctly.");
        }
    }
}
