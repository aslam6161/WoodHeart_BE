using System.Text.Json;
using WoodHeart.Service.Infrastructure.Notifications;
using WoodHeart.Service.Services.Notifications;

namespace WoodHeart.Tests.Notifications;

/// <summary>
/// The two messages addressed to the shop rather than to a customer.
/// </summary>
/// <remarks>
/// <para>
/// <b>They are sent more often than any other message, so the part count is
/// the whole design.</b> One on every order and one on every consultation
/// request, at roughly a third of a taka per part — a wording that slips past
/// 160 characters, or acquires one Bangla character and drops the part to 70,
/// turns a sensible expense into a reason to switch the message off. That is
/// the failure being guarded against: not a broken message, a message the shop
/// turns off and then misses orders.
/// </para>
/// <para>
/// <b>The number in the body is the customer's; the number it is sent to is
/// the shop's.</b> Conflating them would put a customer's telephone number in
/// the "sent to" column of the admin screen, and would have the shop ringing
/// itself.
/// </para>
/// </remarks>
public class ShopAlertTemplateTests
{
    private const string ShopPhone = "01799990000";

    private static string OrderReceived(string paymentMethod = "cod", string? email = null) =>
        JsonSerializer.Serialize(new
        {
            orderNumber = "WH-2609-00042",
            contactName = "Rakib Hasan",
            customerPhone = "+8801712349999",
            grandTotal = 254100m,
            currency = "BDT",
            paymentMethod,
            itemCount = 3,
            address = "House 12, Road 3, Dhanmondi, Dhaka",
            recipientPhone = ShopPhone,
            recipientEmail = email
        });

    private static string BookingReceived(string? email = null) =>
        JsonSerializer.Serialize(new
        {
            bookingNumber = "WHC-2609-00017",
            contactName = "Rakib Hasan",
            customerPhone = "+8801712349999",
            serviceName = "Home consultation",
            scheduledAt = "Tuesday 30 September, 4:00 PM",
            recipientPhone = ShopPhone,
            recipientEmail = email
        });

    private static RenderedNotification Render(string type, string payload) =>
        NotificationTemplates.Render(type, payload, ShopPhone)!.Value;

    // -------------------------------------------------------------------------
    // A new order
    // -------------------------------------------------------------------------

    [Fact]
    public void An_order_tells_the_shop_what_it_needs_to_act_on_it()
    {
        var rendered = Render(NotificationTemplates.OrderReceived, OrderReceived());

        rendered.SmsText.ShouldNotBeNull();
        rendered.SmsText.ShouldContain("WH-2609-00042");
        rendered.SmsText.ShouldContain("BDT 254,100");
        rendered.SmsText.ShouldContain("3 item(s)");

        // The first thing anybody does is ring the customer to confirm the
        // address. A message that sent them to the panel for the number would
        // have failed at being the thing you read on a telephone.
        rendered.SmsText.ShouldContain("Rakib Hasan");
        rendered.SmsText.ShouldContain("+8801712349999");
    }

    [Fact]
    public void And_whether_there_is_cash_to_collect()
    {
        Render(NotificationTemplates.OrderReceived, OrderReceived())
            .SmsText!.ShouldContain("cash on delivery");

        Render(NotificationTemplates.OrderReceived, OrderReceived(paymentMethod: "bkash"))
            .SmsText!.ShouldContain("paid online");
    }

    [Fact]
    public void It_goes_to_the_shop_and_not_to_the_customer()
    {
        // The one mistake with a consequence outside the shop: sending the
        // staff message to the person who bought the thing.
        var rendered = Render(NotificationTemplates.OrderReceived, OrderReceived());

        rendered.RecipientPhone.ShouldBe(ShopPhone);
        rendered.RecipientPhone.ShouldNotBe("+8801712349999");
    }

    [Fact]
    public void A_shop_with_no_email_address_still_gets_the_text()
    {
        var rendered = Render(NotificationTemplates.OrderReceived, OrderReceived());

        rendered.RecipientEmail.ShouldBeNull();
        rendered.SmsText.ShouldNotBeNullOrWhiteSpace();
        rendered.HasSomethingToSay.ShouldBeTrue();
    }

    // -------------------------------------------------------------------------
    // A new consultation
    // -------------------------------------------------------------------------

    [Fact]
    public void A_booking_tells_the_shop_when_somebody_is_expecting_them()
    {
        var rendered = Render(NotificationTemplates.BookingReceived, BookingReceived());

        rendered.SmsText.ShouldNotBeNull();
        rendered.SmsText.ShouldContain("WHC-2609-00017");

        // The first question is always whether anybody is free then.
        rendered.SmsText.ShouldContain("Tuesday 30 September, 4:00 PM");
        rendered.SmsText.ShouldContain("+8801712349999");

        rendered.RecipientPhone.ShouldBe(ShopPhone);
    }

    [Fact]
    public void And_says_that_somebody_has_to_confirm_it()
    {
        // The customer has already been told the shop will confirm shortly. A
        // message that did not say so would be read as "an appointment has been
        // made", and nobody would do anything.
        var rendered = Render(NotificationTemplates.BookingReceived, BookingReceived());

        rendered.SmsText!.ShouldContain("Confirm");
        rendered.EmailHtml.ShouldNotBeNull();
        rendered.EmailHtml.ShouldContain("waiting to be confirmed");
    }

    // -------------------------------------------------------------------------
    // What they cost
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("cod")]
    [InlineData("bkash")]
    public void An_order_alert_costs_one_billed_part(string paymentMethod)
    {
        var rendered = Render(
            NotificationTemplates.OrderReceived, OrderReceived(paymentMethod: paymentMethod));

        SmsParts.Count(rendered.SmsText).ShouldBe(
            1, $"the shop pays for this on every order: \"{rendered.SmsText}\"");
    }

    [Fact]
    public void So_does_a_booking_alert()
    {
        var rendered = Render(NotificationTemplates.BookingReceived, BookingReceived());

        SmsParts.Count(rendered.SmsText).ShouldBe(1, rendered.SmsText);
    }

    [Theory]
    [InlineData("order.received")]
    [InlineData("booking.received")]
    public void Neither_contains_a_character_that_would_quadruple_its_cost(string type)
    {
        // A dash, a curly quote or a taka sign switches the whole message to
        // UCS-2, where a part is 70 characters rather than 160 — so one
        // typographic nicety would turn a one-part message into three.
        var payload = type == "order.received" ? OrderReceived() : BookingReceived();

        var text = Render(type, payload).SmsText!;

        text.Where(c => c > 127).ToList()
            .ShouldBeEmpty($"\"{text}\" would be billed at 70 characters a part");
    }

    // -------------------------------------------------------------------------
    // The email
    // -------------------------------------------------------------------------

    [Fact]
    public void The_email_closes_by_naming_the_customer_rather_than_the_shop()
    {
        // The shared footer says "Any questions, please call …" with the shop's
        // own number, which addressed to the shop is an instruction to ring
        // itself.
        var rendered = Render(
            NotificationTemplates.OrderReceived, OrderReceived(email: "shop@example.com"));

        rendered.RecipientEmail.ShouldBe("shop@example.com");
        rendered.EmailHtml.ShouldNotBeNull();
        rendered.EmailHtml.ShouldContain("Call the customer on +8801712349999.");
        rendered.EmailHtml.ShouldNotContain("Any questions, please call");
    }

    [Fact]
    public void A_customer_message_still_closes_the_way_it_always_did()
    {
        // The footer gained a parameter; the messages that never passed one
        // must be untouched.
        var rendered = NotificationTemplates.Render(
            NotificationTemplates.OrderPlaced,
            JsonSerializer.Serialize(new
            {
                orderNumber = "WH-2609-00042",
                contactName = "Rakib Hasan",
                contactPhone = "+8801712349999",
                contactEmail = "rakib@example.com",
                language = "en",
                grandTotal = 254100m,
                itemCount = 3,
                paymentMethod = "cod",
                address = "Dhanmondi, Dhaka"
            }),
            ShopPhone)!.Value;

        rendered.EmailHtml!.ShouldContain($"Any questions, please call {ShopPhone}.");
    }
}
