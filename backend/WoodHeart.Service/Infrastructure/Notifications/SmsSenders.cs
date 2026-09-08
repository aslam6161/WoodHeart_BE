using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Settings;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Service.Interfaces.Notifications;

namespace WoodHeart.Service.Infrastructure.Notifications;

/// <summary>
/// The sender used when no gateway is configured.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not a stub, and not a mistake.</b> A fresh clone has no SMS credentials
/// and must still be able to place an order end to end. The alternatives are
/// both worse: failing every notification fills the outbox with red on a
/// developer machine, and silently dropping them means nobody notices the day
/// the real gateway stops working.
/// </para>
/// <para>
/// So this writes what it would have sent, at Information, with the recipient
/// masked. The log line is the evidence, and the parts count is real — it is
/// the same arithmetic the gateway will bill on, so the cost of a Bangla
/// message is visible before a single taka is spent.
/// </para>
/// </remarks>
public class LoggingSmsSender(ILogger<LoggingSmsSender> logger) : ISmsSender
{
    public Task<GeneralResponse<SmsDeliveryReceipt>> SendAsync(
        PhoneNumber recipient, string message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipient);

        var parts = SmsParts.Count(message);

        NotificationLog.SmsNotSent(logger, recipient.Masked, parts, message);

        return Task.FromResult(GeneralResponse<SmsDeliveryReceipt>.Success(
            new SmsDeliveryReceipt($"logged-{Guid.NewGuid():n}", parts, null)));
    }
}

/// <summary>
/// Alpha SMS (api.sms.net.bd), the gateway the shop starts on.
/// </summary>
/// <remarks>
/// <para>
/// One gateway, chosen because it is widely used here, prices per part, and
/// takes a plain form post. <b>Swapping it is one class</b> — everything above
/// this talks to <see cref="ISmsSender"/>, and nothing else in the codebase
/// knows the provider's name. If the shop ends up on BulkSMSBD or SSL Wireless
/// instead, this file is what changes.
/// </para>
/// <para>
/// <b>The API key never appears in a log or an error message.</b> It is a
/// spending credential: anyone holding it can empty the shop's SMS balance. The
/// failure path below reports the gateway's own message and the status code,
/// and nothing else.
/// </para>
/// <para>
/// Errors come back as <c>external.</c> codes, which
/// <c>BaseApiController.HandleResult</c> maps to 502 — the honest answer when
/// somebody else's service is the thing that failed.
/// </para>
/// </remarks>
public class AlphaSmsSender(
    HttpClient http,
    IOptions<SmsSettings> options,
    ILogger<AlphaSmsSender> logger) : ISmsSender
{
    private readonly SmsSettings _settings = options.Value;

    public async Task<GeneralResponse<SmsDeliveryReceipt>> SendAsync(
        PhoneNumber recipient, string message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipient);

        var parts = SmsParts.Count(message);

        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["api_key"] = _settings.ApiKey,
            ["msg"] = message,

            // National format without the +880. The gateway is domestic and
            // rejects the international prefix on some accounts.
            ["to"] = recipient.National
        };

        if (!string.IsNullOrWhiteSpace(_settings.SenderId))
        {
            fields["sender_id"] = _settings.SenderId;
        }

        try
        {
            using var content = new FormUrlEncodedContent(fields);
            using var response = await http.PostAsync(
                new Uri("/sendsms", UriKind.Relative), content, cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                NotificationLog.SmsGatewayRefused(
                    logger, recipient.Masked, (int)response.StatusCode, Trim(body));

                return Failed($"The SMS gateway returned {(int)response.StatusCode}.");
            }

            // Alpha replies 200 with {"error":N,"msg":"...","data":{...}} even
            // when it has refused the message, so the status code alone is not
            // an answer.
            var (ok, reported, messageId) = ReadReply(body);

            if (!ok)
            {
                NotificationLog.SmsGatewayRefused(logger, recipient.Masked, 200, Trim(reported));

                return Failed(string.IsNullOrWhiteSpace(reported)
                    ? "The SMS gateway refused the message."
                    : reported);
            }

            return GeneralResponse<SmsDeliveryReceipt>.Success(
                new SmsDeliveryReceipt(messageId ?? "unknown", parts, null));
        }
        catch (HttpRequestException ex)
        {
            NotificationLog.SmsGatewayUnreachable(logger, ex, recipient.Masked);

            return Failed("The SMS gateway could not be reached.");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // A timeout, not a caller cancelling. Distinguished because one is
            // a gateway problem worth retrying and the other is a shutdown.
            NotificationLog.SmsGatewayUnreachable(logger, ex, recipient.Masked);

            return Failed("The SMS gateway timed out.");
        }
    }

    /// <summary>Reads Alpha's envelope: <c>error</c> is 0 on success.</summary>
    private static (bool Ok, string Reported, string? MessageId) ReadReply(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            var error = root.TryGetProperty("error", out var code) && code.TryGetInt32(out var value)
                ? value
                : -1;

            var reported = root.TryGetProperty("msg", out var msg) ? msg.GetString() ?? string.Empty : string.Empty;

            var id = root.TryGetProperty("data", out var data)
                     && data.ValueKind == JsonValueKind.Object
                     && data.TryGetProperty("request_id", out var requestId)
                ? requestId.ToString()
                : null;

            return (error == 0, reported, id);
        }
        catch (JsonException)
        {
            // A gateway that has started returning an HTML error page. Worth
            // retrying, and worth not crashing the whole batch over.
            return (false, "The SMS gateway returned something that was not JSON.", null);
        }
    }

    private static GeneralResponse<SmsDeliveryReceipt> Failed(string message) =>
        GeneralResponse<SmsDeliveryReceipt>.Fail(NotificationErrors.SmsUnavailable, message);

    /// <summary>Enough of the gateway's reply to diagnose it, and no more.</summary>
    private static string Trim(string body) =>
        body.Length <= 300 ? body : body[..300];
}

/// <summary>
/// How many parts a message will be billed as.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is money, not trivia.</b> A GSM-7 message is 160 characters, or 153
/// per part once it is split. One Bangla character switches the whole message
/// to UCS-2, where the limits are <b>70</b> and 67 — so adding a single "৳" to
/// an English message can double its cost.
/// </para>
/// <para>
/// Deliberately conservative: anything outside the plain ASCII the GSM-7
/// alphabet mostly covers is treated as Unicode. Over-counting a rare message
/// is cheaper than under-counting every Bangla one.
/// </para>
/// </remarks>
public static class SmsParts
{
    private const int GsmSingle = 160;
    private const int GsmConcatenated = 153;
    private const int UnicodeSingle = 70;
    private const int UnicodeConcatenated = 67;

    public static int Count(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return 0;
        }

        // Surrogate pairs count as two UCS-2 units, which is what the gateway
        // bills on — an emoji in a message is two characters, not one.
        var units = message.Length;
        var unicode = message.Any(c => c > 127);

        var single = unicode ? UnicodeSingle : GsmSingle;
        var concatenated = unicode ? UnicodeConcatenated : GsmConcatenated;

        return units <= single
            ? 1
            : (int)Math.Ceiling(units / (double)concatenated);
    }

    /// <summary>For a log line: <c>2 parts (Unicode)</c>.</summary>
    public static string Describe(string? message) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Count(message)} part(s) ({(message?.Any(c => c > 127) == true ? "Unicode" : "GSM-7")})");
}
