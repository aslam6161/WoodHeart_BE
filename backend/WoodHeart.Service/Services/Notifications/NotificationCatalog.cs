using System.Text.Json;
using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Enums.Notifications;
using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Service.Services.Notifications;

/// <summary>One kind of message, described for somebody deciding whether to send it.</summary>
/// <param name="WhenItFires">In words, because "order.status_changed" is not an answer.</param>
/// <param name="SupportsBangla">
/// Whether the wording has a Bangla form. The two messages addressed to the
/// shop do not — they go to whoever runs it, and an English one fits in a
/// single billed part where a Bangla one would take three.
/// </param>
public readonly record struct NotificationCatalogEntry(
    string Code,
    string Name,
    string WhenItFires,
    NotificationAudience Audience,
    bool SupportsBangla);

/// <summary>
/// What each message is, and one of each to look at.
/// </summary>
/// <remarks>
/// <para>
/// <b>The sample payloads are here so the admin screen can show the real
/// thing.</b> Every message below is rendered by the same
/// <see cref="NotificationTemplates"/> the delivery worker uses, from a payload
/// shaped like the ones the services enqueue — so what an admin reads before
/// switching a template on is the message, not a description of it, down to
/// the part count.
/// </para>
/// <para>
/// <b>That part count is the decision.</b> An English confirmation fits in one
/// billed part at 160 characters; the same message in Bangla is billed every
/// <b>70</b>, so it is usually three. A shop choosing which messages are worth
/// sending by SMS cannot make that call from a template name, and it is not a
/// call anybody should make by sending one and reading the invoice.
/// </para>
/// <para>
/// Pure and static, like the templates themselves. The sample fields mirror
/// what the enqueuing services write; a field renamed on one side without the
/// other shows up as a gap in the preview rather than a wrong message to a
/// customer, which is the right place for that mistake to surface.
/// </para>
/// </remarks>
public static class NotificationCatalog
{
    public static readonly IReadOnlyList<NotificationCatalogEntry> All =
    [
        new(NotificationTemplates.OrderPlaced,
            "Order confirmation",
            "As soon as a customer places an order.",
            NotificationAudience.Customer,
            SupportsBangla: true),

        new(NotificationTemplates.OrderStatusChanged,
            "Order update",
            "When an order is confirmed, shipped, delivered or cancelled. Other "
            + "statuses are the shop's own bookkeeping and send nothing.",
            NotificationAudience.Customer,
            SupportsBangla: true),

        new(NotificationTemplates.PaymentStatusChanged,
            "Payment receipt",
            "When the shop records money received or refunded. Most orders here "
            + "are cash at the door, so this is the only receipt either side has.",
            NotificationAudience.Customer,
            SupportsBangla: true),

        new(NotificationTemplates.OrderReceived,
            "New order, to the shop",
            "As soon as a customer places an order, to whoever runs the shop.",
            NotificationAudience.Shop,
            SupportsBangla: false),

        new(NotificationTemplates.BookingReceived,
            "New consultation, to the shop",
            "When a customer asks for a consultation, so somebody knows to confirm it.",
            NotificationAudience.Shop,
            SupportsBangla: false),

        new(NotificationTemplates.BookingRequested,
            "Consultation received",
            "When a customer asks for a consultation — before anybody at the shop "
            + "has agreed to it.",
            NotificationAudience.Customer,
            SupportsBangla: true),

        new(NotificationTemplates.BookingStatusChanged,
            "Consultation update",
            "When a consultation is confirmed, moved or cancelled. Completed and "
            + "no-show send nothing.",
            NotificationAudience.Customer,
            SupportsBangla: true),

        new(NotificationTemplates.BookingReminder,
            "Consultation reminder",
            "A day before the appointment, and again shortly beforehand.",
            NotificationAudience.Customer,
            SupportsBangla: true),

        new(NotificationTemplates.QuotationSent,
            "Quotation sent",
            "When a quotation is sent to the customer.",
            NotificationAudience.Customer,
            SupportsBangla: true),

        new(NotificationTemplates.QuotationConverted,
            "Quotation became an order",
            "When an accepted quotation is turned into an order. Sent instead of "
            + "the ordinary order confirmation, not as well as it.",
            NotificationAudience.Customer,
            SupportsBangla: true),

        new(NotificationTemplates.QuotationAnswered,
            "Quotation answered",
            "When a customer accepts or declines a quotation. Goes to the shop, "
            + "and carries the reason they gave for saying no.",
            NotificationAudience.Shop,
            SupportsBangla: false),

        new(NotificationTemplates.StockLow,
            "Low stock digest",
            "Each morning, to the shop, when anything is at or below its reorder "
            + "level.",
            NotificationAudience.Shop,
            SupportsBangla: false)
    ];

    public static NotificationCatalogEntry? Find(string? code)
    {
        foreach (var entry in All)
        {
            if (string.Equals(entry.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// A representative payload for one template, in one language.
    /// </summary>
    /// <remarks>
    /// Deliberately ordinary — a two-item cash order, a consultation on a
    /// Tuesday afternoon. A preview built from the longest name and the largest
    /// total in the shop would over-count every part and talk somebody out of a
    /// message that costs one.
    /// </remarks>
    public static string? Sample(string? code, string language)
    {
        var bangla = string.Equals(
            language, GlobalConstants.BanglaLanguage, StringComparison.OrdinalIgnoreCase);

        var name = bangla ? "রফিকুল ইসলাম" : "Rafiqul Islam";

        return Find(code)?.Code switch
        {
            NotificationTemplates.OrderPlaced => Json(new
            {
                orderNumber = "WH-2609-00042",
                contactName = name,
                contactPhone = SamplePhone,
                contactEmail = SampleEmail,
                language,
                grandTotal = 24500m,
                currency = GlobalConstants.Currency,
                paymentMethod = PaymentMethodCodes.CashOnDelivery,
                itemCount = 2,
                address = bangla
                    ? "বাড়ি ১২, রোড ৫, ধানমন্ডি, ঢাকা"
                    : "House 12, Road 5, Dhanmondi, Dhaka"
            }),

            // Shipped rather than Confirmed, because it is the one that changes
            // with the payment status: an unpaid order tells the customer what
            // to have ready for the rider, and that sentence is the longest
            // this template produces.
            NotificationTemplates.OrderStatusChanged => Json(new
            {
                orderNumber = "WH-2609-00042",
                status = "Shipped",
                contactName = name,
                contactPhone = SamplePhone,
                contactEmail = SampleEmail,
                language,
                grandTotal = 24500m,
                currency = GlobalConstants.Currency,
                paymentStatus = "Unpaid"
            }),

            // Paid rather than a refund: it is the message this template
            // sends on nearly every order, and the one whose length matters.
            NotificationTemplates.PaymentStatusChanged => Json(new
            {
                orderNumber = "WH-2609-00042",
                contactName = name,
                contactPhone = SamplePhone,
                contactEmail = SampleEmail,
                language,
                grandTotal = 24500m,
                currency = GlobalConstants.Currency,
                status = "Paid",
                paymentMethod = PaymentMethodCodes.CashOnDelivery
            }),

            NotificationTemplates.OrderReceived => Json(new
            {
                orderNumber = "WH-2609-00042",
                contactName = "Rafiqul Islam",

                // Named apart from the recipient on purpose: this message goes
                // to the shop, and the number in the body is the customer's.
                customerPhone = SamplePhone,
                grandTotal = 24500m,
                currency = GlobalConstants.Currency,
                paymentMethod = PaymentMethodCodes.CashOnDelivery,
                itemCount = 2,
                address = "House 12, Road 5, Dhanmondi, Dhaka",
                recipientPhone = SampleShopPhone,
                recipientEmail = SampleEmail
            }),

            NotificationTemplates.BookingReceived => Json(new
            {
                bookingNumber = "WHC-2609-00017",
                contactName = "Rafiqul Islam",
                customerPhone = SamplePhone,
                serviceName = "Home consultation",
                scheduledAt = SampleSlot,
                recipientPhone = SampleShopPhone,
                recipientEmail = SampleEmail
            }),

            NotificationTemplates.BookingRequested or NotificationTemplates.BookingReminder => Json(new
            {
                bookingNumber = "WHC-2609-00017",
                contactName = name,
                contactPhone = SamplePhone,
                contactEmail = SampleEmail,
                language,
                serviceName = bangla ? "বাড়িতে পরামর্শ" : "Home consultation",
                status = "Confirmed",
                scheduledAt = SampleSlot,
                durationMinutes = 60,
                fee = 1500m,
                advanceDue = (decimal?)null
            }),

            NotificationTemplates.BookingStatusChanged => Json(new
            {
                bookingNumber = "WHC-2609-00017",
                contactName = name,
                contactPhone = SamplePhone,
                contactEmail = SampleEmail,
                language,
                serviceName = bangla ? "বাড়িতে পরামর্শ" : "Home consultation",
                status = "Confirmed",
                scheduledAt = SampleSlot,
                durationMinutes = 60,
                fee = 1500m,
                advanceDue = (decimal?)null
            }),

            NotificationTemplates.QuotationSent => Json(new
            {
                quotationNumber = "WHQ-2609-00008",
                contactName = name,
                contactPhone = SamplePhone,
                contactEmail = SampleEmail,
                language,
                grandTotal = 186000m,
                currency = GlobalConstants.Currency,
                validUntil = "8 October",
                lineCount = 4,
                orderNumber = (string?)null
            }),

            NotificationTemplates.QuotationConverted => Json(new
            {
                quotationNumber = "WHQ-2609-00008",
                contactName = name,
                contactPhone = SamplePhone,
                contactEmail = SampleEmail,
                language,
                grandTotal = 186000m,
                currency = GlobalConstants.Currency,
                validUntil = "8 October",
                lineCount = 4,
                orderNumber = "WH-2609-00043"
            }),

            // Declined rather than accepted: it is the longer of the two,
            // being the one that carries a reason, so it is the one whose part
            // count on the screen is worth looking at.
            NotificationTemplates.QuotationAnswered => Json(new
            {
                quotationNumber = "WHQ-2609-00008",
                answer = "Declined",
                contactName = name,
                customerPhone = SamplePhone,
                grandTotal = 245000m,
                currency = GlobalConstants.Currency,
                reason = "Found the same thing cheaper at another shop in Gulshan.",
                recipientPhone = SamplePhone,
                recipientEmail = SampleEmail
            }),

            NotificationTemplates.StockLow => Json(new
            {
                date = "2026-09-24",
                total = 4,
                lines = new object[]
                {
                    new { product = "Teak dining table", variant = "6 seat / Natural", sku = "TDT-6-NAT", stocked = true, available = 1, reorderLevel = 3 },
                    new { product = "Rattan armchair", variant = "Walnut", sku = "RAC-WAL", stocked = true, available = 0, reorderLevel = 2 },
                    new { product = "Mango wood shelf", variant = "4 tier", sku = "MWS-4T", stocked = true, available = 2, reorderLevel = 4 },
                    new { product = "Cane headboard", variant = "King", sku = "CHB-K", stocked = false, available = 0, reorderLevel = 2 }
                },
                recipientPhone = SamplePhone,
                recipientEmail = SampleEmail
            }),

            _ => null
        };
    }

    /// <summary>
    /// What the outbox row is about, for a list somebody scans.
    /// </summary>
    /// <remarks>
    /// The reference comes out of the idempotency key rather than the payload,
    /// and that is not a shortcut: every enqueuing service builds its key from
    /// the number the message concerns — <c>order.placed:WH-2609-00042</c> —
    /// so the key is indexed, already unique, and searchable without reading
    /// jsonb. A row with no key falls back to the payload.
    /// </remarks>
    public static string? Reference(string? idempotencyKey, string? payload)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var parts = idempotencyKey.Split(':');

            if (parts.Length > 1 && parts[1].Length > 0)
            {
                return parts[1];
            }
        }

        return Field(payload, "orderNumber")
               ?? Field(payload, "bookingNumber")
               ?? Field(payload, "quotationNumber");
    }

    /// <summary>The recipient's number, masked, so a list of failures names nobody in full.</summary>
    public static string? MaskedRecipient(string? payload)
    {
        var raw = Field(payload, "contactPhone") ?? Field(payload, "recipientPhone");

        return PhoneNumber.TryParse(raw, out var phone) && phone is not null
            ? phone.Masked
            : null;
    }

    private const string SamplePhone = "01712345678";

    private const string SampleEmail = "customer@example.com";

    /// <summary>The shop's own number, for the two messages addressed to it.</summary>
    private const string SampleShopPhone = "01799990000";

    private const string SampleSlot = "Tuesday 30 September, 4:00 PM";

    private static string Json(object value) => JsonSerializer.Serialize(value);

    private static string? Field(string? payload, string name)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);

            return document.RootElement.TryGetProperty(name, out var value)
                   && value.ValueKind == JsonValueKind.String
                   && value.GetString() is { Length: > 0 } text
                ? text
                : null;
        }
        catch (JsonException)
        {
            // A payload that will not parse is exactly the sort of row somebody
            // is looking at this screen to find. It should render without its
            // reference, not refuse to render.
            return null;
        }
    }
}
