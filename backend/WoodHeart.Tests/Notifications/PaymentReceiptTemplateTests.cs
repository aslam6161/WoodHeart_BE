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
            paymentMethod = "cod"
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
    public void An_advance_says_what_is_still_owed_rather_than_a_figure()
    {
        // The application does not carry how much the advance was, so it says
        // the thing it does know: the rest is due at the door.
        var rendered = Render("AdvancePaid");

        var text = rendered.SmsText!;

        text.ShouldContain("balance is due on delivery");
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
    public void A_part_refund_names_no_figure_and_asks_them_to_call()
    {
        // The amount lives in the shop's own note, and quoting the order total
        // here would tell somebody they had been refunded five times what they
        // were.
        var rendered = Render("PartiallyRefunded");

        var text = rendered.SmsText!;

        text.ShouldNotContain("BDT 24,500");
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
