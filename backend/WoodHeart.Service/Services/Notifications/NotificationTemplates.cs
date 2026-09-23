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
    /// <summary>To the shop, not a customer: the lines at or below their reorder level.</summary>
    public const string StockLow = "stock.low";

    /// <summary>A consultation asked for. The shop confirms it separately.</summary>
    public const string BookingRequested = "booking.requested";

    /// <summary>A consultation confirmed, moved or called off.</summary>
    public const string BookingStatusChanged = "booking.status_changed";

    /// <summary>"Your consultation is at four" — sent twice before it happens.</summary>
    public const string BookingReminder = "booking.reminder";

    /// <summary>A quotation put in front of the customer.</summary>
    public const string QuotationSent = "quotation.sent";

    /// <summary>It became an order, and here is the number to quote.</summary>
    public const string QuotationConverted = "quotation.converted";

    public static readonly IReadOnlyList<string> KnownTypes =
    [
        OrderPlaced,
        OrderStatusChanged,
        StockLow,
        BookingRequested,
        BookingStatusChanged,
        BookingReminder,
        QuotationSent,
        QuotationConverted
    ];

    public static RenderedNotification? Render(string type, string payload, string shopPhone)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        var rendered = type switch
        {
            OrderPlaced => RenderOrderPlaced(root, shopPhone.Trim()),
            OrderStatusChanged => RenderStatusChanged(root, shopPhone.Trim()),
            StockLow => RenderStockLow(root, shopPhone.Trim()),
            BookingRequested => RenderBookingRequested(root, shopPhone.Trim()),
            BookingStatusChanged => RenderBookingStatusChanged(root, shopPhone.Trim()),
            BookingReminder => RenderBookingReminder(root, shopPhone.Trim()),
            QuotationSent => RenderQuotationSent(root, shopPhone.Trim()),
            QuotationConverted => RenderQuotationConverted(root, shopPhone.Trim()),
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
    // stock.low — to the shop
    // -------------------------------------------------------------------------

    /// <summary>
    /// The morning list. The SMS names the first few lines and counts the
    /// rest — a Bangla-free message, because it goes to whoever runs the
    /// shop and it has to fit in a part or two. The email carries the whole
    /// table.
    /// </summary>
    private static RenderedNotification RenderStockLow(JsonElement root, string shopPhone)
    {
        var total = Int_(root, "total");
        var lines = root.TryGetProperty("lines", out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(l => (
                Product: String_(l, "product"),
                Variant: String_(l, "variant"),
                Sku: String_(l, "sku"),
                Available: Int_(l, "available"),
                Stocked: !l.TryGetProperty("stocked", out var s) || s.ValueKind != JsonValueKind.False)).ToList()
            : [];

        static string Count(int available, bool stocked) =>
            !stocked ? "never stocked" : available <= 0 ? "sold out" : $"{available} left";

        var named = lines.Take(3)
            .Select(l => $"{l.Product} ({l.Variant}) {Count(l.Available, l.Stocked)}");
        var more = total > 3 ? $" and {total - 3} more" : string.Empty;

        var sms = $"WoodHeart stock: {total} line{(total == 1 ? string.Empty : "s")} low - "
                  + string.Join(", ", named) + more + ". See Stock in the admin.";

        var rows = string.Concat(lines.Select(l =>
            $"<tr><td style=\"padding:6px 8px;border-bottom:1px solid #eee\">{Escape(l.Product)}<br>"
            + $"<span style=\"color:#8a7f75;font-size:13px\">{Escape(l.Variant)} · {Escape(l.Sku)}</span></td>"
            + $"<td style=\"padding:6px 8px;border-bottom:1px solid #eee;text-align:right;white-space:nowrap\">"
            + $"{Escape(Count(l.Available, l.Stocked))}</td></tr>"));

        var table = $"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" "
                    + $"style=\"font-size:15px;border-collapse:collapse\">{rows}</table>";

        var body = Email(
            heading: "Low stock",
            greeting: total == 1 ? "One line is at or below its reorder level." : $"{total} lines are at or below their reorder level.",
            lines: [table, "Open <strong>Stock</strong> in the admin to record a stock-in."],
            shopPhone: shopPhone,
            bangla: false);

        return new RenderedNotification(
            sms,
            $"Low stock: {total} line{(total == 1 ? string.Empty : "s")} — {String_(root, "date")}",
            body,
            String_(root, "recipientPhone"),
            NullIfBlank(String_(root, "recipientEmail")));
    }

    // -------------------------------------------------------------------------
    // booking.requested
    // -------------------------------------------------------------------------

    /// <summary>
    /// "We have your request" — not "you are booked".
    /// </summary>
    /// <remarks>
    /// The wording matters: a booking is Requested until somebody at the shop
    /// looks at it, and a message that said "confirmed" would have a customer
    /// turning up to a studio nobody was expecting them at.
    /// </remarks>
    private static RenderedNotification RenderBookingRequested(JsonElement root, string shopPhone)
    {
        var number = String_(root, "bookingNumber");
        var name = String_(root, "contactName");
        var service = String_(root, "serviceName");
        var when = String_(root, "scheduledAt");
        var bangla = IsBangla(root);

        var sms = bangla
            ? $"WoodHeart: {when} সময়ের জন্য বুকিং {number} পেয়েছি। শীঘ্রই নিশ্চিত করব। {shopPhone}"
            : $"WoodHeart: booking {number} received for {when}. We will confirm shortly. {shopPhone}";

        var subject = bangla ? $"বুকিং {number} পেয়েছি" : $"We have your booking {number}";

        var body = Email(
            heading: bangla ? "ধন্যবাদ!" : "Thank you!",
            greeting: bangla ? $"প্রিয় {Escape(name)}," : $"Dear {Escape(name)},",
            lines:
            [
                bangla
                    ? $"আমরা আপনার <strong>{Escape(service)}</strong> বুকিং অনুরোধ পেয়েছি।"
                    : $"We have received your request for a <strong>{Escape(service)}</strong>.",
                bangla
                    ? $"সময়: <strong>{Escape(when)}</strong>"
                    : $"When: <strong>{Escape(when)}</strong>",
                bangla
                    ? $"বুকিং নম্বর: <strong>{Escape(number)}</strong>"
                    : $"Booking number: <strong>{Escape(number)}</strong>",
                bangla
                    ? "আমরা নিশ্চিত করার পর আপনাকে জানাব।"
                    : "We will write again once it is confirmed."
            ],
            shopPhone: shopPhone,
            bangla: bangla);

        return new RenderedNotification(
            sms, subject, body, String_(root, "contactPhone"), NullIfBlank(String_(root, "contactEmail")));
    }

    // -------------------------------------------------------------------------
    // booking.status_changed
    // -------------------------------------------------------------------------

    /// <summary>
    /// The moves a customer should hear about, and only those.
    /// </summary>
    /// <remarks>
    /// Completed and NoShow are the shop's own bookkeeping. An SMS saying "your
    /// consultation is marked complete" is a message nobody needs and a part
    /// the shop pays for; one saying "you did not attend" is worse than
    /// useless. Returning null suppresses the message, which is what the
    /// dispatcher does with anything it cannot render.
    /// </remarks>
    private static RenderedNotification? RenderBookingStatusChanged(JsonElement root, string shopPhone)
    {
        var number = String_(root, "bookingNumber");
        var name = String_(root, "contactName");
        var when = String_(root, "scheduledAt");
        var status = String_(root, "status");
        var bangla = IsBangla(root);

        var (sms, subject, line) = status switch
        {
            "Confirmed" => (
                bangla
                    ? $"WoodHeart: বুকিং {number} নিশ্চিত, {when}। {shopPhone}"
                    : $"WoodHeart: booking {number} is confirmed for {when}. {shopPhone}",
                bangla ? $"বুকিং {number} নিশ্চিত" : $"Booking {number} confirmed",
                bangla
                    ? "আপনার পরামর্শ সেশনটি নিশ্চিত করা হয়েছে।"
                    : "Your consultation is confirmed."),

            "Rescheduled" => (
                bangla
                    ? $"WoodHeart: বুকিং {number} সরানো হয়েছে, নতুন সময় {when}। {shopPhone}"
                    : $"WoodHeart: booking {number} has moved to {when}. {shopPhone}",
                bangla ? $"বুকিং {number} সরানো হয়েছে" : $"Booking {number} has moved",
                bangla
                    ? "আপনার পরামর্শ সেশনের সময় পরিবর্তন করা হয়েছে।"
                    : "Your consultation has been moved to a new time."),

            "Cancelled" => (
                bangla
                    ? $"WoodHeart: বুকিং {number} বাতিল করা হয়েছে। {shopPhone}"
                    : $"WoodHeart: booking {number} has been cancelled. {shopPhone}",
                bangla ? $"বুকিং {number} বাতিল" : $"Booking {number} cancelled",
                bangla
                    ? "আপনার পরামর্শ সেশনটি বাতিল করা হয়েছে।"
                    : "Your consultation has been cancelled."),

            _ => (string.Empty, string.Empty, string.Empty)
        };

        if (sms.Length == 0)
        {
            return null;
        }

        var body = Email(
            heading: bangla ? "বুকিং হালনাগাদ" : "Booking update",
            greeting: bangla ? $"প্রিয় {Escape(name)}," : $"Dear {Escape(name)},",
            lines:
            [
                line,
                bangla
                    ? $"বুকিং নম্বর: <strong>{Escape(number)}</strong>"
                    : $"Booking number: <strong>{Escape(number)}</strong>",
                status == "Cancelled"
                    ? string.Empty
                    : bangla
                        ? $"সময়: <strong>{Escape(when)}</strong>"
                        : $"When: <strong>{Escape(when)}</strong>"
            ],
            shopPhone: shopPhone,
            bangla: bangla);

        return new RenderedNotification(
            sms, subject, body, String_(root, "contactPhone"), NullIfBlank(String_(root, "contactEmail")));
    }

    // -------------------------------------------------------------------------
    // booking.reminder
    // -------------------------------------------------------------------------

    /// <summary>
    /// The reminder, sent the day before and again shortly beforehand.
    /// </summary>
    /// <remarks>
    /// <b>It states the time rather than saying "tomorrow".</b> The reminders
    /// are bands, not moments — somebody who books at nine for four the same
    /// afternoon gets the first one straight away — so "tomorrow" would be
    /// wrong often enough to matter, and a customer who turns up on the wrong
    /// day because of a word is a customer the shop has lost.
    /// </remarks>
    private static RenderedNotification RenderBookingReminder(JsonElement root, string shopPhone)
    {
        var number = String_(root, "bookingNumber");
        var name = String_(root, "contactName");
        var service = String_(root, "serviceName");
        var when = String_(root, "scheduledAt");
        var bangla = IsBangla(root);

        var sms = bangla
            ? $"WoodHeart: মনে করিয়ে দিচ্ছি, আপনার বুকিং {number} এর সময় {when}। {shopPhone}"
            : $"WoodHeart: a reminder that your booking {number} is at {when}. {shopPhone}";

        var subject = bangla
            ? $"মনে করিয়ে দিচ্ছি: বুকিং {number}"
            : $"A reminder: booking {number}";

        var body = Email(
            heading: bangla ? "মনে করিয়ে দিচ্ছি" : "A reminder",
            greeting: bangla ? $"প্রিয় {Escape(name)}," : $"Dear {Escape(name)},",
            lines:
            [
                bangla
                    ? $"আপনার <strong>{Escape(service)}</strong> সেশনটি আসছে।"
                    : $"Your <strong>{Escape(service)}</strong> is coming up.",
                bangla
                    ? $"সময়: <strong>{Escape(when)}</strong>"
                    : $"When: <strong>{Escape(when)}</strong>",
                bangla
                    ? $"বুকিং নম্বর: <strong>{Escape(number)}</strong>"
                    : $"Booking number: <strong>{Escape(number)}</strong>",
                bangla
                    ? "আসতে না পারলে দয়া করে আগেই জানান।"
                    : "If you cannot make it, please tell us beforehand."
            ],
            shopPhone: shopPhone,
            bangla: bangla);

        return new RenderedNotification(
            sms, subject, body, String_(root, "contactPhone"), NullIfBlank(String_(root, "contactEmail")));
    }

    // -------------------------------------------------------------------------
    // quotation.sent
    // -------------------------------------------------------------------------

    /// <summary>
    /// "Here is what it would cost, and until when."
    /// </summary>
    /// <remarks>
    /// <b>The date is in the SMS, not only the email.</b> A quotation that has
    /// run out is re-quoted rather than honoured, and somebody who finds that
    /// out when they try to accept has been treated badly. Many customers here
    /// read no email at all, so the one line they will certainly see has to
    /// carry it.
    /// </remarks>
    private static RenderedNotification RenderQuotationSent(JsonElement root, string shopPhone)
    {
        var number = String_(root, "quotationNumber");
        var name = String_(root, "contactName");
        var total = Taka(Decimal_(root, "grandTotal"));
        var until = String_(root, "validUntil");
        var bangla = IsBangla(root);

        var sms = bangla
            ? $"WoodHeart: কোটেশন {number}, {total}। {until} পর্যন্ত বৈধ। {shopPhone}"
            : $"WoodHeart: quotation {number} is {total}, good until {until}. {shopPhone}";

        var subject = bangla ? $"কোটেশন {number}" : $"Your quotation {number}";

        var body = Email(
            heading: bangla ? "আপনার কোটেশন" : "Your quotation",
            greeting: bangla ? $"প্রিয় {Escape(name)}," : $"Dear {Escape(name)},",
            lines:
            [
                bangla
                    ? $"মোট: <strong>{Escape(total)}</strong>"
                    : $"Total: <strong>{Escape(total)}</strong>",
                bangla
                    ? $"বৈধতা: <strong>{Escape(until)}</strong> পর্যন্ত"
                    : $"Good until: <strong>{Escape(until)}</strong>",
                bangla
                    ? $"কোটেশন নম্বর: <strong>{Escape(number)}</strong>"
                    : $"Quotation number: <strong>{Escape(number)}</strong>",
                bangla
                    ? "রাজি থাকলে আমাদের জানান, আমরা অর্ডার তৈরি করে দেব।"
                    : "Tell us if you are happy with it and we will turn it into an order."
            ],
            shopPhone: shopPhone,
            bangla: bangla);

        return new RenderedNotification(
            sms, subject, body, String_(root, "contactPhone"), NullIfBlank(String_(root, "contactEmail")));
    }

    // -------------------------------------------------------------------------
    // quotation.converted
    // -------------------------------------------------------------------------

    /// <summary>"It is an order now, and this is its number."</summary>
    /// <remarks>
    /// Sent instead of the ordinary order confirmation, not as well as it: the
    /// customer has already seen these figures on the quotation, and two
    /// messages about one purchase is one message too many — and one billed
    /// part the shop need not pay for.
    /// </remarks>
    private static RenderedNotification RenderQuotationConverted(JsonElement root, string shopPhone)
    {
        var number = String_(root, "quotationNumber");
        var orderNumber = String_(root, "orderNumber");
        var name = String_(root, "contactName");
        var total = Taka(Decimal_(root, "grandTotal"));
        var bangla = IsBangla(root);

        var sms = bangla
            ? $"WoodHeart: কোটেশন {number} এখন অর্ডার {orderNumber}। আমরা শীঘ্রই যোগাযোগ করব। {shopPhone}"
            : $"WoodHeart: quotation {number} is now order {orderNumber}. We will be in touch. {shopPhone}";

        var subject = bangla ? $"অর্ডার {orderNumber}" : $"Your order {orderNumber}";

        var body = Email(
            heading: bangla ? "ধন্যবাদ!" : "Thank you!",
            greeting: bangla ? $"প্রিয় {Escape(name)}," : $"Dear {Escape(name)},",
            lines:
            [
                bangla
                    ? $"কোটেশন <strong>{Escape(number)}</strong> থেকে অর্ডার তৈরি হয়েছে।"
                    : $"We have made an order from quotation <strong>{Escape(number)}</strong>.",
                bangla
                    ? $"অর্ডার নম্বর: <strong>{Escape(orderNumber)}</strong>"
                    : $"Order number: <strong>{Escape(orderNumber)}</strong>",
                bangla
                    ? $"মোট: <strong>{Escape(total)}</strong>"
                    : $"Total: <strong>{Escape(total)}</strong>"
            ],
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
