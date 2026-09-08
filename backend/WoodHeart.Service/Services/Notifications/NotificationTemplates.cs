using System.Globalization;
using System.Text.Json;
using WoodHeart.Domain.Constants;

namespace WoodHeart.Service.Services.Notifications;

/// <summary>What a notification comes to, once its payload has been read.</summary>
/// <param name="SmsText">
/// The message body. Null when this notification has nothing to say by SMS.
/// </param>
/// <param name="EmailSubject">Null when there is nothing to email.</param>
/// <param name="EmailHtml">Null when there is nothing to email.</param>
/// <param name="RecipientPhone">As stored on the order — E.164, or unparseable.</param>
/// <param name="RecipientEmail">Null on most orders in this market.</param>
public readonly record struct RenderedNotification(
    string? SmsText,
    string? EmailSubject,
    string? EmailHtml,
    string? RecipientPhone,
    string? RecipientEmail)
{
    public bool HasSomethingToSay => SmsText is not null || EmailSubject is not null;
}

/// <summary>
/// Turns an outbox payload into the words a customer reads.
/// </summary>
/// <remarks>
/// <para>
/// Pure and static, for the reason <c>CartPricer</c> and
/// <c>OrderStatusMachine</c> are: the wording a customer receives is worth
/// being able to read in a test without a gateway, a database or a clock. It
/// is also the part most likely to be edited by somebody who is not thinking
/// about transactions.
/// </para>
/// <para>
/// <b>Length is a cost here, not a style question.</b> SMS is billed per part:
/// 160 characters in GSM-7, but only <b>70</b> once a message contains a single
/// Bangla character, because the whole message switches to UCS-2. A two-line
/// Bangla confirmation is three billed parts. Every message below is written
/// to stay inside one English part, and the Bangla ones are kept as short as
/// they can be while still saying the thing.
/// </para>
/// <para>
/// <b>Money is written with a thousands separator and no decimals.</b> "BDT
/// 254,100" is what a person reads back; "254100.00" is what a database
/// column holds, and 254,100.00 wastes three characters of a billed part on
/// two zeroes nobody needs.
/// </para>
/// </remarks>
public static class NotificationTemplates
{
    public const string OrderPlaced = "order.placed";
    public const string OrderStatusChanged = "order.status_changed";

    /// <summary>
    /// Every type this renders. A message of any other type is suppressed
    /// rather than retried — see <c>OutboxDispatcher</c>.
    /// </summary>
    public static readonly IReadOnlyList<string> KnownTypes = [OrderPlaced, OrderStatusChanged];

    public static RenderedNotification? Render(string type, string payload, string shopPhone)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        var rendered = type switch
        {
            OrderPlaced => RenderOrderPlaced(root, shopPhone.Trim()),
            OrderStatusChanged => RenderStatusChanged(root, shopPhone.Trim()),
            _ => (RenderedNotification?)null
        };

        // Every message ends with the shop's phone number, and store.phone may
        // not be set. Trimmed rather than guarded at each of the dozen call
        // sites — a trailing space is a character of a billed part, and on a
        // Bangla message a part is 70 characters.
        return rendered is { } value
            ? value with { SmsText = value.SmsText?.TrimEnd() }
            : null;
    }

    // -------------------------------------------------------------------------
    // order.placed
    // -------------------------------------------------------------------------

    private static RenderedNotification RenderOrderPlaced(JsonElement root, string shopPhone)
    {
        var number = String_(root, "orderNumber");
        var name = String_(root, "contactName");
        var total = Taka(Decimal_(root, "grandTotal"));
        var items = Int_(root, "itemCount");
        var cash = string.Equals(
            String_(root, "paymentMethod"), PaymentMethodCodes.CashOnDelivery, StringComparison.OrdinalIgnoreCase);

        var sms = IsBangla(root)
            ? cash
                ? $"WoodHeart: অর্ডার {number} নিশ্চিত। {items}টি পণ্য, {total}। ডেলিভারিতে নগদ পরিশোধ। {shopPhone}"
                : $"WoodHeart: অর্ডার {number} নিশ্চিত। {items}টি পণ্য, {total}। {shopPhone}"
            : cash
                ? $"WoodHeart: Order {number} confirmed. {items} item(s), {total}. Please pay cash on delivery. {shopPhone}"
                : $"WoodHeart: Order {number} confirmed. {items} item(s), {total}. {shopPhone}";

        var subject = IsBangla(root)
            ? $"আপনার WoodHeart অর্ডার {number}"
            : $"Your WoodHeart order {number}";

        var body = Email(
            heading: IsBangla(root) ? "ধন্যবাদ!" : "Thank you!",
            greeting: IsBangla(root) ? $"প্রিয় {Escape(name)}," : $"Dear {Escape(name)},",
            lines:
            [
                IsBangla(root)
                    ? $"আমরা আপনার অর্ডার <strong>{Escape(number)}</strong> পেয়েছি।"
                    : $"We have received your order <strong>{Escape(number)}</strong>.",
                IsBangla(root)
                    ? $"{items}টি পণ্য — সর্বমোট <strong>{total}</strong>।"
                    : $"{items} item(s) — total <strong>{total}</strong>.",
                Escape(String_(root, "address")),
                cash
                    ? IsBangla(root)
                        ? "ডেলিভারির সময় নগদ পরিশোধ করবেন।"
                        : "Payable in cash when the order is delivered."
                    : string.Empty
            ],
            shopPhone: shopPhone,
            bangla: IsBangla(root));

        return new RenderedNotification(
            sms, subject, body, String_(root, "contactPhone"), NullIfBlank(String_(root, "contactEmail")));
    }

    // -------------------------------------------------------------------------
    // order.status_changed
    // -------------------------------------------------------------------------

    private static RenderedNotification RenderStatusChanged(JsonElement root, string shopPhone)
    {
        var number = String_(root, "orderNumber");
        var name = String_(root, "contactName");
        var status = String_(root, "status");
        var total = Taka(Decimal_(root, "grandTotal"));
        var bangla = IsBangla(root);

        // Only mention cash when there is still cash to collect. Telling
        // somebody who has already paid to have the money ready is the sort of
        // message that produces a phone call.
        var owing = string.Equals(String_(root, "paymentStatus"), "Unpaid", StringComparison.Ordinal);

        var (sms, subject, line) = status switch
        {
            "Confirmed" => (
                bangla
                    ? $"WoodHeart: অর্ডার {number} নিশ্চিত হয়েছে। {shopPhone}"
                    : $"WoodHeart: Order {number} is confirmed. {shopPhone}",
                bangla ? $"অর্ডার {number} নিশ্চিত" : $"Order {number} confirmed",
                bangla ? "আপনার অর্ডারটি গ্রহণ করা হয়েছে।" : "Your order has been accepted."),

            "Shipped" => (
                bangla
                    ? owing
                        ? $"WoodHeart: অর্ডার {number} পথে আছে। {total} প্রস্তুত রাখুন। {shopPhone}"
                        : $"WoodHeart: অর্ডার {number} পথে আছে। {shopPhone}"
                    : owing
                        ? $"WoodHeart: Order {number} is on its way. Please have {total} ready for the rider. {shopPhone}"
                        : $"WoodHeart: Order {number} is on its way. {shopPhone}",
                bangla ? $"অর্ডার {number} পথে" : $"Order {number} is on its way",
                bangla ? "আপনার অর্ডারটি ডেলিভারির জন্য বেরিয়েছে।" : "Your order has left for delivery."),

            "Delivered" => (
                bangla
                    ? $"WoodHeart: অর্ডার {number} ডেলিভারি সম্পন্ন। ধন্যবাদ!"
                    : $"WoodHeart: Order {number} has been delivered. Thank you!",
                bangla ? $"অর্ডার {number} ডেলিভারি সম্পন্ন" : $"Order {number} delivered",
                bangla ? "আপনার অর্ডারটি পৌঁছে দেওয়া হয়েছে।" : "Your order has been delivered."),

            "Cancelled" => (
                bangla
                    ? $"WoodHeart: অর্ডার {number} বাতিল হয়েছে। প্রশ্ন থাকলে কল করুন {shopPhone}"
                    : $"WoodHeart: Order {number} has been cancelled. Please call {shopPhone} if this is unexpected.",
                bangla ? $"অর্ডার {number} বাতিল" : $"Order {number} cancelled",
                bangla ? "আপনার অর্ডারটি বাতিল করা হয়েছে।" : "Your order has been cancelled."),

            // A status nobody wrote words for. Silence is the right answer: the
            // shop moving an order to ReadyToShip is not news, and inventing a
            // message from the enum name would send "Order WH-... is now
            // PartiallyFulfilled" to a customer.
            _ => (null, null, null)
        };

        if (sms is null)
        {
            return new RenderedNotification(
                null, null, null, String_(root, "contactPhone"), NullIfBlank(String_(root, "contactEmail")));
        }

        var body = Email(
            heading: bangla ? $"অর্ডার {Escape(number)}" : $"Order {Escape(number)}",
            greeting: bangla ? $"প্রিয় {Escape(name)}," : $"Dear {Escape(name)},",
            lines: [line!, bangla ? $"সর্বমোট <strong>{total}</strong>।" : $"Total <strong>{total}</strong>."],
            shopPhone: shopPhone,
            bangla: bangla);

        return new RenderedNotification(
            sms, subject, body, String_(root, "contactPhone"), NullIfBlank(String_(root, "contactEmail")));
    }

    // -------------------------------------------------------------------------
    // Shared
    // -------------------------------------------------------------------------

    /// <summary>
    /// A plain, single-column email.
    /// </summary>
    /// <remarks>
    /// Inline styles and a table, because that is what survives the mail
    /// clients people actually read on. No images and no external stylesheet:
    /// a transactional email that renders as a broken-image icon on a phone
    /// with images off has failed at the one thing it exists to do.
    /// </remarks>
    private static string Email(
        string heading, string greeting, string[] lines, string shopPhone, bool bangla)
    {
        var paragraphs = string.Concat(
            lines.Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => $"<p style=\"margin:0 0 12px;font-size:15px;line-height:1.5\">{l}</p>"));

        var footer = bangla
            ? $"যেকোনো প্রশ্নে কল করুন {Escape(shopPhone)}।"
            : $"Any questions, please call {Escape(shopPhone)}.";

        return $"""
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0"
                   style="background:#f6f5f3;padding:24px 0;font-family:Segoe UI,Arial,sans-serif;color:#2b2622">
              <tr><td align="center">
                <table role="presentation" width="560" cellpadding="0" cellspacing="0"
                       style="background:#ffffff;border-radius:8px;padding:32px;max-width:560px">
                  <tr><td>
                    <h1 style="margin:0 0 4px;font-size:20px;font-weight:600">{heading}</h1>
                    <p style="margin:0 0 20px;font-size:13px;color:#8a7f75">WoodHeart</p>
                    <p style="margin:0 0 12px;font-size:15px">{greeting}</p>
                    {paragraphs}
                    <p style="margin:24px 0 0;font-size:13px;color:#8a7f75">{footer}</p>
                  </td></tr>
                </table>
              </td></tr>
            </table>
            """;
    }

    private static bool IsBangla(JsonElement root) =>
        string.Equals(String_(root, "language"), GlobalConstants.BanglaLanguage, StringComparison.OrdinalIgnoreCase);

    /// <summary>"BDT 254,100" — grouped, and without the two zeroes nobody reads.</summary>
    private static string Taka(decimal amount) =>
        string.Create(CultureInfo.InvariantCulture, $"BDT {amount:N0}");

    private static string String_(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static decimal Decimal_(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetDecimal(out var amount) ? amount : 0m;

    private static int Int_(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var count) ? count : 0;

    private static string? NullIfBlank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// HTML-escapes a value that came out of the payload.
    /// </summary>
    /// <remarks>
    /// The address and the shop phone are the two fields here that a customer
    /// typed. An address line containing <c>&lt;</c> would otherwise break the
    /// email's markup, and a name is not a place to find out whether the mail
    /// client sanitises anything.
    /// </remarks>
    private static string Escape(string value) =>
        System.Net.WebUtility.HtmlEncode(value);
}
