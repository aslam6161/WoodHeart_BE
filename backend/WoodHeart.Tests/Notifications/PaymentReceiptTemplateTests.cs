using System.Text.Json;
using WoodHeart.Service.Infrastructure.Notifications;
using WoodHeart.Service.Services.Notifications;

namespace WoodHeart.Tests.Notifications;

/// <summary>
/// The receipt for money in, and for money back.
/// </summary>
/// <remarks>
/// <para>
/// <b>Most orders here are cash handed to a rider at the door.</b> The customer
/// has no receipt and no statement to check against, so this message is the
/// only record either side holds that the money changed hands — which makes it
/// as much the shop's protection as the customer's.
/// </para>
/// <para>
/// The rules with teeth: <b>a figure is named only when it is the whole
/// order</b>, because a wrong number on a refund is worse than no number; and
/// <b>two of the five statuses say nothing</b>, because announcing a payment
/// that failed at a gateway nobody has built would be a lie, and announcing
/// that an order is unpaid announces the absence of an event.
/// </para>
/// </remarks>
public class PaymentReceiptTemplateTests
{
    private const string ShopPhone = "01712345678";

    private const string Type = "payment.status_changed";

    private static string Payload(string status, string language = "en") =>
        JsonSerializer.Serialize(new
        {
            orderNumber = "WH-2609-00042",
            status,
            contactName = "Rakib Hasan",
            contactPhone = "+8801712349999",
            contactEmail = (string?)null,
            language,
            grandTotal = 24500m,
            currency = "BDT",
            paymentMethod = "cod",
            amountPaid = 10000m,
            amountOutstanding = 14500m
        });

    private static RenderedNotification Render(string status, string language = "en") =>
        NotificationTemplates.Render(Type, Payload(status, language), ShopPhone)!.Value;

    // -------------------------------------------------------------------------
    // Money in
    // -------------------------------------------------------------------------

    [Fact]
    public void A_paid_order_is_receipted_with_the_amount()
    {
        var rendered = Render("Paid");

        rendered.SmsText.ShouldNotBeNull();
        rendered.SmsText.ShouldContain("BDT 24,500");
        rendered.SmsText.ShouldContain("WH-2609-00042");

        // The number to ring if the customer disagrees, which is the whole
        // reason this message is worth its part.
        rendered.SmsText.ShouldContain(ShopPhone);
    }

    [Fact]
    public void An_advance_names_what_arrived_and_what_is_left()
    {
        // This message used to say only that an advance had arrived, because
        // the application had nowhere to record how much. The ledger is what
        // changed: PaymentStatus.AdvancePaid said something came, and
        // Order.RequiredAdvanceAmount said what was asked for, and between
        // them they never said what was actually taken.
        var rendered = Render("AdvancePaid");

        var text = rendered.SmsText!;

        text.ShouldContain("BDT 10,000");
        text.ShouldContain("BDT 14,500");

        // Not the order total. Quoting that would tell somebody who paid a
        // tenth of it that the whole thing is settled.
        text.ShouldNotContain("BDT 24,500");
    }

    // -------------------------------------------------------------------------
    // Money back
    // -------------------------------------------------------------------------

    [Fact]
    public void A_full_refund_names_the_amount_because_it_is_the_whole_order()
    {
        var rendered = Render("Refunded");

        var text = rendered.SmsText!;

        text.ShouldContain("BDT 24,500");
        text.ShouldContain("refunded");
    }

    [Fact]
    public void A_part_refund_says_what_the_shop_still_holds()
    {
        // Still not the order total — quoting that would tell somebody they
        // had been refunded five times what they were. What it can say now is
        // the figure the customer actually wants: what is left with the shop.
        var rendered = Render("PartiallyRefunded");

        var text = rendered.SmsText!;

        text.ShouldNotContain("BDT 24,500");
        text.ShouldContain("BDT 10,000");
        text.ShouldContain("part of your payment");
        text.ShouldContain(ShopPhone);
    }

    // -------------------------------------------------------------------------
    // The silences
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("Unpaid")]
    [InlineData("Failed")]
    public void Nothing_is_sent_for_a_status_that_is_not_news(string status)
    {
        // Unpaid is where an order starts. Failed belongs to a gateway nobody
        // has written, and telling somebody their payment failed when nothing
        // tried to take it would be a lie.
        var rendered = Render(status);

        rendered.SmsText.ShouldBeNull();
        rendered.HasSomethingToSay.ShouldBeFalse();
    }

    [Fact]
    public void And_the_dispatcher_can_still_see_who_it_was_for()
    {
        // Suppressed rather than unrenderable: the row still carries a
        // recipient, so the admin screen can say which customer was not
        // written to and why.
        var rendered = Render("Unpaid");

        rendered.RecipientPhone.ShouldBe("+8801712349999");
    }

    // -------------------------------------------------------------------------
    // What it costs
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("Paid")]
    [InlineData("AdvancePaid")]
    [InlineData("Refunded")]
    [InlineData("PartiallyRefunded")]
    public void Every_receipt_fits_in_one_English_part(string status)
    {
        var rendered = Render(status);

        SmsParts.Count(rendered.SmsText).ShouldBe(1, rendered.SmsText);
    }

    [Theory]
    [InlineData("Paid")]
    [InlineData("Refunded")]
    public void And_the_Bangla_form_says_the_same_thing(string status)
    {
        var bangla = Render(status, "bn");

        var text = bangla.SmsText!;

        text.ShouldContain("WH-2609-00042");
        text.ShouldContain("BDT 24,500");
        text.ShouldNotBe(Render(status).SmsText);

        // Unicode, so a part is 70 characters. Three would be the cost of a
        // sentence nobody trimmed.
        SmsParts.Count(text).ShouldBeLessThanOrEqualTo(2, text);
    }

    // -------------------------------------------------------------------------
    // The delivery that is also the receipt
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("en")]
    [InlineData("bn")]
    public void Delivering_a_cash_order_names_the_money_in_the_same_message(string language)
    {
        // This is how nearly every order here is paid: the rider hands it over
        // and takes the cash. Marking it delivered records the payment, and
        // the message that was going out anyway becomes the receipt — one
        // billed part rather than two, and the figure rather than a second
        // text about it a moment later.
        var rendered = Delivered("Paid", "cod", language);

        var text = rendered.SmsText!;

        text.ShouldContain("BDT 24,500");
        SmsParts.Count(text).ShouldBe(1, text);
    }

    [Fact]
    public void Delivering_an_order_paid_days_ago_does_not()
    {
        // The money arrived at checkout. "Your payment of BDT 24,500 received"
        // on the doorstep reads as a second charge.
        var text = Delivered("Paid", "bkash").SmsText!;

        text.ShouldNotContain("BDT 24,500");
        text.ShouldContain("delivered");
    }

    [Fact]
    public void Nor_does_one_delivered_with_the_money_still_owing()
    {
        var text = Delivered("Unpaid", "cod").SmsText!;

        text.ShouldNotContain("received");
    }

    private static RenderedNotification Delivered(
        string paymentStatus, string paymentMethod, string language = "en") =>
        NotificationTemplates.Render(
            "order.status_changed",
            JsonSerializer.Serialize(new
            {
                orderNumber = "WH-2609-00042",
                status = "Delivered",
                contactName = "Rakib Hasan",
                contactPhone = "+8801712349999",
                contactEmail = (string?)null,
                language,
                grandTotal = 24500m,
                currency = "BDT",
                paymentStatus,
                paymentMethod
            }),
            ShopPhone)!.Value;

    [Fact]
    public void The_email_carries_the_order_total_either_way()
    {
        var rendered = NotificationTemplates.Render(
            Type,
            JsonSerializer.Serialize(new
            {
                orderNumber = "WH-2609-00042",
                status = "Paid",
                contactName = "Rakib Hasan",
                contactPhone = "+8801712349999",
                contactEmail = "rakib@example.com",
                language = "en",
                grandTotal = 24500m,
                currency = "BDT",
                paymentMethod = "cod"
            }),
            ShopPhone)!.Value;

        rendered.RecipientEmail.ShouldBe("rakib@example.com");
        rendered.EmailSubject!.ShouldContain("WH-2609-00042");
        rendered.EmailHtml!.ShouldContain("BDT 24,500");
    }
}
