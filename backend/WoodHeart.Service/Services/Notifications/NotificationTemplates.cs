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

    /// <summary>A receipt: money in, or money back.</summary>
    /// <remarks>
    /// Most orders here are cash handed to a rider at the door. The customer
    /// has no receipt and no statement to check against, so the only record
    /// either side has that the money changed hands is the one the shop sends
    /// — which is as much the shop's protection as the customer's.
    /// </remarks>
    public const string PaymentStatusChanged = "payment.status_changed";

    /// <summary>A basket somebody filled and walked away from.</summary>
    /// <remarks>
    /// Furniture is a considered purchase and a basket left overnight is
    /// ordinary rather than a lost sale — but a basket nobody ever mentions
    /// again usually is one. Sent once per basket, never twice.
    /// </remarks>
    public const string CartAbandoned = "cart.abandoned";

    /// <summary>To the shop, not a customer: the lines at or below their reorder level.</summary>
    public const string StockLow = "stock.low";

    /// <summary>To the shop: somebody has just bought something.</summary>
    /// <remarks>
    /// The shop is small and whoever runs it is on their telephone rather than
    /// in front of the panel. An order placed at nine in the evening and seen
    /// the following afternoon is a customer who waited a day for no reason,
    /// and nothing in the application used to say a word about it.
    /// </remarks>
    public const string OrderReceived = "order.received";

    /// <summary>A consultation asked for. The shop confirms it separately.</summary>
    public const string BookingRequested = "booking.requested";

    /// <summary>To the shop: somebody is asking for a consultation.</summary>
    /// <remarks>
    /// A booking stays Requested until a person at the shop looks at it, and
    /// the customer has already been told "we will confirm shortly". Nobody
    /// having been told to look is how that sentence becomes untrue.
    /// </remarks>
    public const string BookingReceived = "booking.received";

    /// <summary>A consultation confirmed, moved or called off.</summary>
    public const string BookingStatusChanged = "booking.status_changed";

    /// <summary>"Your consultation is at four" — sent twice before it happens.</summary>
    public const string BookingReminder = "booking.reminder";

    /// <summary>A quotation put in front of the customer.</summary>
    public const string QuotationSent = "quotation.sent";

    /// <summary>It became an order, and here is the number to quote.</summary>
    public const string QuotationConverted = "quotation.converted";

    /// <summary>To the shop: the customer has said yes, or said no.</summary>
    /// <remarks>
    /// A quotation is the largest single thing this shop sells — a fitted
    /// wardrobe, a room of furniture — and answering it is the customer's move,
    /// made on their own time. Accepted at eleven at night is timber that could
    /// have been ordered first thing; declined is the one moment the customer
    /// will ever say why, and it was being written to a column nobody reads.
    /// </remarks>
    public const string QuotationAnswered = "quotation.answered";

    /// <summary>
    /// Every type this renders. A message of any other type is suppressed
    /// rather than retried — see <c>OutboxDispatcher</c>.
    /// </summary>
    public static readonly IReadOnlyList<string> KnownTypes =
    [
        OrderPlaced,
        OrderStatusChanged,
        PaymentStatusChanged,
        OrderReceived,
        StockLow,
        BookingRequested,
        BookingReceived,
        BookingStatusChanged,
        BookingReminder,
        QuotationSent,
        QuotationConverted,
        QuotationAnswered,
        CartAbandoned
    ];

    public static RenderedNotification? Render(string type, string payload, string shopPhone)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        var rendered = type switch
        {
            OrderPlaced => RenderOrderPlaced(root, shopPhone.Trim()),
            OrderStatusChanged => RenderStatusChanged(root, shopPhone.Trim()),
            PaymentStatusChanged => RenderPaymentChanged(root, shopPhone.Trim()),
            OrderReceived => RenderOrderReceived(root),
            BookingReceived => RenderBookingReceived(root),
            StockLow => RenderStockLow(root, shopPhone.Trim()),
            BookingRequested => RenderBookingRequested(root, shopPhone.Trim()),
            BookingStatusChanged => RenderBookingStatusChanged(root, shopPhone.Trim()),
            BookingReminder => RenderBookingReminder(root, shopPhone.Trim()),
            QuotationSent => RenderQuotationSent(root, shopPhone.Trim()),
            QuotationConverted => RenderQuotationConverted(root, shopPhone.Trim()),
            QuotationAnswered => RenderQuotationAnswered(root),
            CartAbandoned => RenderCartAbandoned(root, shopPhone.Trim()),
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

        // Marking a cash order delivered records the money at the same moment,
        // and this message is already going out. Naming the amount here makes
        // it the receipt — one billed part instead of two, and the customer
        // gets the figure rather than a separate text about it a second later.
        var cashJustCollected =
            string.Equals(String_(root, "paymentStatus"), "Paid", StringComparison.Ordinal)
            && string.Equals(
                String_(root, "paymentMethod"),
                PaymentMethodCodes.CashOnDelivery,
                StringComparison.OrdinalIgnoreCase);

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
                    ? cashJustCollected
                        // No "thank you" on this one: the Bangla part is 70
                        // characters and the figure is worth more than the
                        // courtesy. The email carries both.
                        ? $"WoodHeart: অর্ডার {number} ডেলিভারি সম্পন্ন, {total} পেয়েছি।"
                        : $"WoodHeart: অর্ডার {number} ডেলিভারি সম্পন্ন। ধন্যবাদ!"
                    : cashJustCollected
                        ? $"WoodHeart: Order {number} has been delivered and {total} received. Thank you!"
                        : $"WoodHeart: Order {number} has been delivered. Thank you!",
                bangla ? $"অর্ডার {number} ডেলিভারি সম্পন্ন" : $"Order {number} delivered",
                bangla
                    ? cashJustCollected
                        ? "আপনার অর্ডারটি পৌঁছে দেওয়া হয়েছে এবং পেমেন্ট পাওয়া গেছে।"
                        : "আপনার অর্ডারটি পৌঁছে দেওয়া হয়েছে।"
                    : cashJustCollected
                        ? "Your order has been delivered and your payment received."
                        : "Your order has been delivered."),

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
    // payment.status_changed
    // -------------------------------------------------------------------------

    /// <summary>
    /// "We have your money", or "you have it back".
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two of the five statuses say nothing, and both silences are
    /// deliberate.</b> <c>Failed</c> belongs to a gateway that is not built
    /// yet; telling somebody their payment failed when nothing tried to take
    /// it would be a lie. <c>Unpaid</c> is where an order starts, so a message
    /// there would announce the absence of an event.
    /// </para>
    /// <para>
    /// <b>The figure is only named when it is the whole order.</b> A part
    /// refund is recorded with its amount in the shop's own note and the
    /// application does not carry it here, so the message says that money has
    /// gone back and asks the customer to call rather than quoting a number it
    /// would be guessing at. A wrong figure on a refund is worse than no
    /// figure.
    /// </para>
    /// </remarks>
    private static RenderedNotification? RenderPaymentChanged(JsonElement root, string shopPhone)
    {
        var number = String_(root, "orderNumber");
        var name = String_(root, "contactName");
        var total = Taka(Decimal_(root, "grandTotal"));
        var status = String_(root, "status");
        var bangla = IsBangla(root);

        // What the ledger says, rather than what the order is worth. On a part
        // payment or a part refund those are different numbers, and quoting the
        // wrong one tells somebody they paid five times what they did.
        var paid = Taka(Decimal_(root, "amountPaid"));
        var owed = Taka(Decimal_(root, "amountOutstanding"));

        var (sms, subject, line) = status switch
        {
            "Paid" => (
                bangla
                    ? $"WoodHeart: অর্ডার {number} এর {total} আমরা পেয়েছি। ধন্যবাদ। {shopPhone}"
                    : $"WoodHeart: we have received {total} for order {number}. Thank you. {shopPhone}",
                bangla ? $"অর্ডার {number} এর পেমেন্ট পেয়েছি" : $"Payment received for order {number}",
                bangla
                    ? "আপনার পেমেন্ট সম্পূর্ণভাবে পাওয়া গেছে।"
                    : "Your payment has been received in full."),

            // The figure the application could not previously hold. Until the
            // ledger existed this message could only say that an advance had
            // arrived, which is the half of it the customer already knew.
            "AdvancePaid" => (
                bangla
                    ? $"WoodHeart: অর্ডার {number} এর অগ্রিম {paid} পেয়েছি। বাকি {owed} ডেলিভারিতে। {shopPhone}"
                    : $"WoodHeart: we have your advance of {paid} for order {number}. {owed} is due on delivery. {shopPhone}",
                bangla ? $"অর্ডার {number} এর অগ্রিম পেয়েছি" : $"Advance received for order {number}",
                bangla
                    ? "বাকি টাকা ডেলিভারির সময় পরিশোধ করবেন।"
                    : "The balance is payable when the order is delivered."),

            "Refunded" => (
                bangla
                    ? $"WoodHeart: অর্ডার {number} এর {total} ফেরত দেওয়া হয়েছে। {shopPhone}"
                    : $"WoodHeart: {total} has been refunded for order {number}. {shopPhone}",
                bangla ? $"অর্ডার {number} ফেরত" : $"Refund for order {number}",
                bangla
                    ? "সম্পূর্ণ টাকা ফেরত দেওয়া হয়েছে।"
                    : "Your payment has been refunded in full."),

            // Also a real figure now: what is left with the shop after the
            // part refund, which is the number the customer wants.
            "PartiallyRefunded" => (
                bangla
                    ? $"WoodHeart: অর্ডার {number} এর কিছু টাকা ফেরত দেওয়া হয়েছে। আমাদের কাছে আছে {paid}। {shopPhone}"
                    : $"WoodHeart: part of your payment for order {number} has been refunded. {paid} remains with us. {shopPhone}",
                bangla ? $"অর্ডার {number} আংশিক ফেরত" : $"Part refund for order {number}",
                bangla
                    ? "আপনার পেমেন্টের একটি অংশ ফেরত দেওয়া হয়েছে।"
                    : "Part of your payment has been refunded."),

            // Unpaid is where an order starts, and Failed belongs to a gateway
            // nobody has written. Neither is news.
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
            lines:
            [
                line!,
                bangla ? $"অর্ডারের মোট <strong>{total}</strong>।" : $"Order total <strong>{total}</strong>."
            ],
            shopPhone: shopPhone,
            bangla: bangla);

        return new RenderedNotification(
            sms, subject, body, String_(root, "contactPhone"), NullIfBlank(String_(root, "contactEmail")));
    }

    // -------------------------------------------------------------------------
    // order.received — to the shop
    // -------------------------------------------------------------------------

    /// <summary>
    /// "Somebody has just bought something."
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>English only, and inside one billed part.</b> It goes to whoever runs
    /// the shop, and a Bangla form would cost three parts on every order rather
    /// than one — which, on a message sent this often, is the difference
    /// between a sensible expense and a reason to switch it off.
    /// </para>
    /// <para>
    /// <b>The customer's number is in it.</b> The first thing anybody does with
    /// a new order is ring the customer to confirm the address, and a message
    /// that made them open the panel to find the number would have failed at
    /// being the thing you read on a telephone.
    /// </para>
    /// <para>
    /// No shop number on the end: this one <em>is</em> the shop. The email
    /// footer carries the customer's instead.
    /// </para>
    /// </remarks>
    private static RenderedNotification RenderOrderReceived(JsonElement root)
    {
        var number = String_(root, "orderNumber");
        var name = String_(root, "contactName");
        var phone = String_(root, "customerPhone");
        var total = Taka(Decimal_(root, "grandTotal"));
        var items = Int_(root, "itemCount");
        var cash = string.Equals(
            String_(root, "paymentMethod"),
            PaymentMethodCodes.CashOnDelivery,
            StringComparison.OrdinalIgnoreCase);

        // A plain hyphen rather than a dash. One character outside plain ASCII
        // switches the whole message to UCS-2 and drops the billed part from
        // 160 characters to 70.
        var sms = $"WoodHeart: new order {number} - {total}, {items} item(s)"
                  + (cash ? ", cash on delivery" : ", paid online")
                  + $". {name} {phone}";

        var body = Email(
            heading: "A new order",
            greeting: $"Order <strong>{Escape(number)}</strong> has just been placed.",
            lines:
            [
                $"{items} item(s) - total <strong>{total}</strong>.",
                cash ? "To be paid in cash on delivery." : "Paid at checkout.",
                $"{Escape(name)} - {Escape(phone)}",
                Escape(String_(root, "address"))
            ],
            shopPhone: string.Empty,
            bangla: false,
            footer: $"Call the customer on {Escape(phone)}.");

        return new RenderedNotification(
            sms,
            $"New order {number} - {total}",
            body,
            String_(root, "recipientPhone"),
            NullIfBlank(String_(root, "recipientEmail")));
    }

    // -------------------------------------------------------------------------
    // booking.received — to the shop
    // -------------------------------------------------------------------------

    /// <summary>
    /// "Somebody is asking for a consultation."
    /// </summary>
    /// <remarks>
    /// The customer has already been told the shop will confirm shortly. This
    /// is what makes that true — and it carries the time, because the first
    /// question is always whether anybody is free then.
    /// </remarks>
    private static RenderedNotification RenderBookingReceived(JsonElement root)
    {
        var number = String_(root, "bookingNumber");
        var name = String_(root, "contactName");
        var phone = String_(root, "customerPhone");
        var service = String_(root, "serviceName");
        var when = String_(root, "scheduledAt");

        var sms = $"WoodHeart: new booking {number} for {when}. {name} {phone}. "
                  + "Confirm it in the admin.";

        var body = Email(
            heading: "A new consultation request",
            greeting: $"Booking <strong>{Escape(number)}</strong> is waiting to be confirmed.",
            lines:
            [
                $"<strong>{Escape(service)}</strong>",
                $"When: <strong>{Escape(when)}</strong>",
                $"{Escape(name)} - {Escape(phone)}",
                "The customer has been told the shop will confirm shortly."
            ],
            shopPhone: string.Empty,
            bangla: false,
            footer: $"Call the customer on {Escape(phone)}.");

        return new RenderedNotification(
            sms,
            $"New booking {number} - {when}",
            body,
            String_(root, "recipientPhone"),
            NullIfBlank(String_(root, "recipientEmail")));
    }

    // -------------------------------------------------------------------------
    // quotation.answered — to the shop
    // -------------------------------------------------------------------------

    /// <summary>
    /// "They have said yes", or "they have said no, and here is why."
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>English only and inside one part</b>, like the other two messages
    /// that go to the shop rather than to a customer.
    /// </para>
    /// <para>
    /// <b>ACCEPTED is shouted and declined is not.</b> This is read on a
    /// telephone screen among everything else that arrives there, and the two
    /// answers want different things done: one is "start buying timber", the
    /// other is "ring them and find out what the trouble was". Making them
    /// look alike would be making the reader do work the message could do.
    /// </para>
    /// <para>
    /// <b>The reason is what gives way.</b> It is typed by the customer and so
    /// has no length anybody controls, and one part is 160 characters. It is
    /// clipped to whatever room is left after the things that must be there —
    /// the number, the money, and somebody to ring — and the email carries it
    /// whole.
    /// </para>
    /// </remarks>
    private static RenderedNotification RenderQuotationAnswered(JsonElement root)
    {
        var number = String_(root, "quotationNumber");
        var name = String_(root, "contactName");
        var phone = String_(root, "customerPhone");
        var total = Taka(Decimal_(root, "grandTotal"));
        var reason = String_(root, "reason").Trim();

        var accepted = string.Equals(
            String_(root, "answer"), "Accepted", StringComparison.OrdinalIgnoreCase);

        // A plain hyphen rather than a dash, for the same reason as everywhere
        // else here: one character outside plain ASCII drops the billed part
        // from 160 characters to 70.
        var sms = $"WoodHeart: quotation {number} {(accepted ? "ACCEPTED" : "declined")}"
                  + $" - {total}. {name} {phone}";

        if (!accepted && reason.Length > 0)
        {
            const string label = " Reason: ";

            // Below about a dozen characters a clipped reason says nothing the
            // reader could act on, so the whole thing is left for the email
            // rather than spending a second part on half a sentence.
            var room = GsmPartLength - sms.Length - label.Length;

            if (room >= 12)
            {
                sms += label + Clip(reason, room);
            }
        }

        var body = Email(
            heading: accepted ? "A quotation was accepted" : "A quotation was declined",
            greeting: accepted
                ? $"Quotation <strong>{Escape(number)}</strong> has been accepted."
                : $"Quotation <strong>{Escape(number)}</strong> has been declined.",
            lines:
            [
                $"Total: <strong>{Escape(total)}</strong>",
                $"{Escape(name)} - {Escape(phone)}",
                reason.Length > 0 ? $"In their words: <em>{Escape(reason)}</em>" : string.Empty,
                accepted
                    ? "Turn it into an order from the quotation screen when you are ready."
                    : string.Empty
            ],
            shopPhone: string.Empty,
            bangla: false,
            footer: $"Call the customer on {Escape(phone)}.");

        return new RenderedNotification(
            sms,
            accepted ? $"Quotation {number} accepted - {total}" : $"Quotation {number} declined",
            body,
            String_(root, "recipientPhone"),
            NullIfBlank(String_(root, "recipientEmail")));
    }

    // -------------------------------------------------------------------------
    // cart.abandoned
    // -------------------------------------------------------------------------

    /// <summary>
    /// "It is still here."
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>No figure.</b> The prices on a basket are a snapshot taken when each
    /// line went in, and the basket re-prices itself when the customer opens
    /// it. Quoting the snapshot in a message sent hours later risks naming a
    /// number the shop will not honour, and being corrected on the doorstep is
    /// worse than never having said it. The email lists what is in the basket
    /// and says plainly that prices are settled at checkout.
    /// </para>
    /// <para>
    /// <b>It names the thing, not the count.</b> "2 items" is a receipt;
    /// "the Segun king bed" is the object somebody stood in a showroom
    /// thinking about. The name is clipped if it has to be — a product name
    /// has no length anybody controls — and the count carries the rest.
    /// </para>
    /// <para>
    /// <b>It asks for a telephone call rather than a click.</b> Half of what
    /// this shop sells is finished on the telephone, and a customer who has
    /// been sitting on a decision for a day is likelier to ring than to find
    /// the basket again.
    /// </para>
    /// </remarks>
    private static RenderedNotification RenderCartAbandoned(JsonElement root, string shopPhone)
    {
        var name = String_(root, "contactName");
        var item = String_(root, "firstItem");
        var others = Int_(root, "otherItems");
        var bangla = IsBangla(root);

        // What the rest of the message costs, so the product name gets whatever
        // is left rather than pushing the whole thing into a second part.
        var tail = bangla
            ? $"। অর্ডার করতে কল করুন {shopPhone}"
            : $" is still in your basket. Call {shopPhone} and we will finish the order for you.";

        var more = others > 0
            ? bangla ? $" ও আরও {others}টি" : $" and {others} more"
            : string.Empty;

        var room = (bangla ? UcsPartLength : GsmPartLength)
            - "WoodHeart: ".Length - tail.Length - more.Length;

        var sms = $"WoodHeart: {Clip(item, Math.Max(room, 8))}{more}{tail}";

        var lines = root.TryGetProperty("items", out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(l => (
                Name: String_(l, "name"),
                Quantity: Int_(l, "quantity"))).ToArray()
            : [];

        var body = Email(
            heading: bangla ? "আপনার বাস্কেট এখনও আছে" : "Your basket is still here",
            greeting: bangla ? $"প্রিয় {Escape(name)}," : $"Dear {Escape(name)},",
            lines:
            [
                bangla
                    ? "আপনি যা রেখে গিয়েছিলেন তা এখনও আপনার বাস্কেটে রয়েছে:"
                    : "What you left is still in your basket:",
                string.Concat(lines.Select(l =>
                    $"&bull; {Escape(l.Name)} &times; {l.Quantity}<br>")),
                bangla
                    ? "চেকআউটের সময় চূড়ান্ত দাম ও ডেলিভারি চার্জ দেখানো হবে।"
                    : "Prices and delivery are settled at checkout.",
                bangla
                    ? $"অর্ডার শেষ করতে কল করুন <strong>{Escape(shopPhone)}</strong>।"
                    : $"Call <strong>{Escape(shopPhone)}</strong> and we will finish the order for you."
            ],
            shopPhone: shopPhone,
            bangla: bangla);

        return new RenderedNotification(
            sms,
            bangla ? "আপনার বাস্কেট এখনও আছে" : "Your basket is still here",
            body,
            String_(root, "contactPhone"),
            NullIfBlank(String_(root, "contactEmail")));
    }

    /// <summary>One billed part of plain ASCII.</summary>
    private const int GsmPartLength = 160;

    /// <summary>
    /// One billed part once a single non-ASCII character appears.
    /// </summary>
    /// <remarks>
    /// A Bangla message is UCS-2 from its first character, and the part drops
    /// from 160 to 70. It is the single most expensive fact in this file.
    /// </remarks>
    private const int UcsPartLength = 70;

    /// <summary>
    /// Cuts to a length without cutting a word in half where it can be helped.
    /// </summary>
    /// <remarks>
    /// No ellipsis. Three dots cost three characters of the very part being
    /// economised, and a sentence that stops is already visibly a sentence that
    /// stopped.
    /// </remarks>
    private static string Clip(string value, int room)
    {
        if (value.Length <= room)
        {
            return value;
        }

        var cut = value[..room];
        var space = cut.LastIndexOf(' ');

        // Only back up to a word boundary when doing so keeps most of the room.
        // A reason whose first word is longer than the space available is cut
        // mid-word rather than thrown away entirely.
        return (space >= room / 2 ? cut[..space] : cut).TrimEnd();
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
    /// <param name="footer">
    /// Replaces "Any questions, please call …". A message addressed to the shop
    /// should not close by telling it to ring itself.
    /// </param>
    private static string Email(
        string heading,
        string greeting,
        string[] lines,
        string shopPhone,
        bool bangla,
        string? footer = null)
    {
        var paragraphs = string.Concat(
            lines.Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => $"<p style=\"margin:0 0 12px;font-size:15px;line-height:1.5\">{l}</p>"));

        footer ??= bangla
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
