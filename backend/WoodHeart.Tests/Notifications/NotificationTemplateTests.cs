using System.Text.Json;
using WoodHeart.Service.Services.Notifications;

namespace WoodHeart.Tests.Notifications;

/// <summary>
/// The words a customer actually receives.
/// </summary>
/// <remarks>
/// <para>
/// Worth testing properly because this is the only part of the system the
/// customer reads, and because <b>length is a cost</b>: SMS is billed per part,
/// 160 characters in GSM-7 and 70 once a single Bangla character appears. A
/// message that grows past a boundary raises the shop's gateway bill on every
/// order from then on, silently.
/// </para>
/// <para>
/// The other rule with teeth: <b>a status nobody wrote words for sends
/// nothing.</b> Composing a message from the enum name would text somebody
/// "Order WH-2609-00042 is now ReadyToShip".
/// </para>
/// </remarks>
public class NotificationTemplateTests
{
    private const string ShopPhone = "01712345678";

    private static string Placed(
        string language = "en",
        string paymentMethod = "cod",
        string? email = null,
        string name = "Rakib Hasan") =>
        JsonSerializer.Serialize(new
        {
            orderNumber = "WH-2609-00042",
            contactName = name,
            contactPhone = "+8801712349999",
            contactEmail = email,
            language,
            grandTotal = 254100m,
            currency = "BDT",
            paymentMethod,
            itemCount = 3,
            address = "House 12, Road 3, Dhanmondi, Dhaka"
        });

    private static string StatusChanged(
        string status, string language = "en", string paymentStatus = "Unpaid") =>
        JsonSerializer.Serialize(new
        {
            orderNumber = "WH-2609-00042",
            status,
            contactName = "Rakib Hasan",
            contactPhone = "+8801712349999",
            contactEmail = (string?)null,
            language,
            grandTotal = 254100m,
            currency = "BDT",
            paymentStatus
        });

    /// <summary>The SMS body, asserted present, so the tests below read as prose.</summary>
    private static string Sms(string type, string payload)
    {
        var rendered = NotificationTemplates.Render(type, payload, ShopPhone);

        rendered.ShouldNotBeNull();
        rendered.Value.SmsText.ShouldNotBeNull();

        return rendered.Value.SmsText;
    }

    // -------------------------------------------------------------------------
    // order.placed
    // -------------------------------------------------------------------------

    [Fact]
    public void A_confirmation_names_the_order_the_count_and_the_total()
    {
        var rendered = NotificationTemplates.Render(
            NotificationTemplates.OrderPlaced, Placed(), ShopPhone)!.Value;

        rendered.SmsText.ShouldNotBeNull();
        rendered.SmsText.ShouldContain("WH-2609-00042");
        rendered.SmsText.ShouldContain("3 item");

        // Grouped and without decimals. "BDT 254,100" is what a person reads
        // back down a phone; the two zeroes are three wasted characters of a
        // billed part.
        rendered.SmsText.ShouldContain("BDT 254,100");
        rendered.SmsText.ShouldNotContain("254100.00");
    }

    [Fact]
    public void A_cash_order_says_so_and_a_prepaid_one_does_not()
    {
        Sms(NotificationTemplates.OrderPlaced, Placed())
            .ShouldContain("cash on delivery");

        Sms(NotificationTemplates.OrderPlaced, Placed(paymentMethod: "bkash"))
            .ShouldNotContain("cash");
    }

    [Fact]
    public void An_English_confirmation_fits_in_one_billed_part()
    {
        // The boundary that costs money. Past 160 characters this becomes two
        // messages on every order the shop ever takes.
        var rendered = NotificationTemplates.Render(
            NotificationTemplates.OrderPlaced, Placed(), ShopPhone)!.Value;

        WoodHeart.Service.Infrastructure.Notifications.SmsParts.Count(rendered.SmsText)
            .ShouldBe(1, $"the message is {rendered.SmsText!.Length} characters: {rendered.SmsText}");
    }

    [Fact]
    public void A_Bangla_confirmation_stays_within_two()
    {
        // Bangla costs a part every 70 characters, so one part is not
        // achievable while still naming the order and the total. Two is the
        // budget; three would be a 50% rise on every Bangla order.
        var rendered = NotificationTemplates.Render(
            NotificationTemplates.OrderPlaced, Placed(language: "bn"), ShopPhone)!.Value;

        WoodHeart.Service.Infrastructure.Notifications.SmsParts.Count(rendered.SmsText)
            .ShouldBeLessThanOrEqualTo(2, $"the message is {rendered.SmsText!.Length} characters");
    }

    [Fact]
    public void The_language_on_the_order_picks_the_language_of_the_message()
    {
        Sms(NotificationTemplates.OrderPlaced, Placed("bn")).ShouldContain("অর্ডার");
        Sms(NotificationTemplates.OrderPlaced, Placed("en")).ShouldContain("Order");
    }

    [Fact]
    public void An_order_with_no_email_address_has_no_email_recipient()
    {
        // Most orders in this market. Not a failure, and not something to
        // retry — see OutboxDispatcher.
        NotificationTemplates.Render(NotificationTemplates.OrderPlaced, Placed(), ShopPhone)!
            .Value.RecipientEmail.ShouldBeNull();

        NotificationTemplates.Render(
                NotificationTemplates.OrderPlaced, Placed(email: "rakib@example.com"), ShopPhone)!
            .Value.RecipientEmail.ShouldBe("rakib@example.com");
    }

    [Fact]
    public void A_name_cannot_break_the_email_markup()
    {
        // The name and the address are the two fields a customer typed. An
        // email body is not the place to discover whether the mail client
        // sanitises anything.
        var rendered = NotificationTemplates.Render(
            NotificationTemplates.OrderPlaced,
            Placed(name: "<script>alert(1)</script>", email: "rakib@example.com"),
            ShopPhone)!.Value;

        rendered.EmailHtml.ShouldNotBeNull();
        rendered.EmailHtml.ShouldNotContain("<script>");
        rendered.EmailHtml.ShouldContain("&lt;script&gt;");
    }

    [Fact]
    public void A_shop_with_no_phone_number_set_does_not_pay_for_a_trailing_space()
    {
        // Every message ends with the shop's number, and store.phone may not be
        // set yet. A trailing space is a character of a billed part — and on a
        // Bangla message a part is seventy characters.
        var rendered = NotificationTemplates.Render(
            NotificationTemplates.OrderPlaced, Placed(), shopPhone: "")!.Value;

        rendered.SmsText.ShouldNotBeNull();
        rendered.SmsText.ShouldBe(rendered.SmsText.TrimEnd());
    }

    // -------------------------------------------------------------------------
    // order.status_changed
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("Confirmed")]
    [InlineData("Shipped")]
    [InlineData("Delivered")]
    [InlineData("Cancelled")]
    public void The_four_moves_a_customer_hears_about_all_have_words(string status)
    {
        var rendered = NotificationTemplates.Render(
            NotificationTemplates.OrderStatusChanged, StatusChanged(status), ShopPhone)!.Value;

        rendered.HasSomethingToSay.ShouldBeTrue();
        rendered.SmsText.ShouldNotBeNull();
        rendered.SmsText.ShouldContain("WH-2609-00042");
    }

    [Theory]
    [InlineData("Processing")]
    [InlineData("ReadyToShip")]
    [InlineData("Returned")]
    [InlineData("PartiallyFulfilled")]
    public void A_status_nobody_wrote_words_for_sends_nothing(string status)
    {
        // Silence, not an invented message. Composing one from the enum name
        // would text a customer "Order WH-2609-00042 is now PartiallyFulfilled".
        var rendered = NotificationTemplates.Render(
            NotificationTemplates.OrderStatusChanged, StatusChanged(status), ShopPhone)!.Value;

        rendered.HasSomethingToSay.ShouldBeFalse();
        rendered.SmsText.ShouldBeNull();
    }

    [Fact]
    public void A_shipping_message_asks_for_the_cash_only_when_cash_is_owed()
    {
        // Telling somebody who has already paid to have 254,100 ready for the
        // rider is the sort of message that produces a phone call.
        Sms(NotificationTemplates.OrderStatusChanged, StatusChanged("Shipped", paymentStatus: "Unpaid"))
            .ShouldContain("BDT 254,100");

        Sms(NotificationTemplates.OrderStatusChanged, StatusChanged("Shipped", paymentStatus: "Paid"))
            .ShouldNotContain("BDT 254,100");
    }

    [Fact]
    public void A_cancellation_gives_the_customer_somebody_to_call()
    {
        Sms(NotificationTemplates.OrderStatusChanged, StatusChanged("Cancelled"))
            .ShouldContain(ShopPhone);
    }

    // -------------------------------------------------------------------------
    // Types
    // -------------------------------------------------------------------------

    [Fact]
    public void A_type_nothing_knows_how_to_render_comes_back_as_nothing()
    {
        // The dispatcher suppresses this rather than retrying it: a template
        // that does not exist will not exist on the fourth attempt either.
        NotificationTemplates.Render("consultation.reminder", Placed(), ShopPhone)
            .ShouldBeNull();
    }

    [Fact]
    public void A_payload_missing_everything_still_renders_rather_than_throwing()
    {
        // A row written by an older version of the code. Producing a thin
        // message beats throwing inside a batch and stalling every other
        // notification behind it.
        var rendered = NotificationTemplates.Render(
            NotificationTemplates.OrderPlaced, "{}", ShopPhone);

        rendered.ShouldNotBeNull();
        rendered.Value.SmsText.ShouldNotBeNull();
    }
}
