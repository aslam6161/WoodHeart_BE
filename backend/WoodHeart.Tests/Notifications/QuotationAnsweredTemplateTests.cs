using System.Text.Json;
using WoodHeart.Service.Infrastructure.Notifications;
using WoodHeart.Service.Services.Notifications;

namespace WoodHeart.Tests.Notifications;

/// <summary>
/// The message that tells the shop a quotation was answered.
/// </summary>
/// <remarks>
/// <para>
/// A quotation is the largest single thing this shop sells, and answering one
/// is the customer's move — made on their own time, from their own telephone,
/// usually in the evening. Until this message existed the answer went no
/// further than a column, so an acceptance worth two hundred thousand taka and
/// a refusal that explained exactly why both waited for somebody to open the
/// panel and notice.
/// </para>
/// <para>
/// The rule with teeth here is <b>the reason gives way, and nothing else
/// does</b>. The reason is typed by a customer and has no length anybody
/// controls; the number, the money and somebody to ring are what the message
/// exists to carry. So the message stays inside one billed part by cutting the
/// reason, never by dropping a field — and the email carries the reason whole.
/// </para>
/// </remarks>
public class QuotationAnsweredTemplateTests
{
    private const string Type = "quotation.answered";

    private const string ShopPhone = "01712345678";

    private const string ShopMobile = "+8801711111111";

    private static RenderedNotification Render(string answer, string? reason = null) =>
        NotificationTemplates.Render(
            Type,
            JsonSerializer.Serialize(new
            {
                quotationNumber = "WHQ-2609-00008",
                answer,
                contactName = "Ayesha Siddiqua",
                customerPhone = "+8801712349999",
                grandTotal = 245000m,
                currency = "BDT",
                reason,
                recipientPhone = ShopMobile,
                recipientEmail = "shop@example.com"
            }),
            ShopPhone)!.Value;

    // -------------------------------------------------------------------------
    // Yes
    // -------------------------------------------------------------------------

    [Fact]
    public void An_acceptance_says_so_where_it_cannot_be_missed()
    {
        var text = Render("Accepted").SmsText!;

        // Shouted, and deliberately. This arrives on a telephone among
        // everything else that arrives there, and it means "start buying
        // timber" — which is not a thing to make somebody read twice.
        text.ShouldContain("ACCEPTED");
        text.ShouldContain("WHQ-2609-00008");
        text.ShouldContain("BDT 245,000");
    }

    [Fact]
    public void And_carries_the_customer_to_ring()
    {
        var text = Render("Accepted").SmsText!;

        text.ShouldContain("Ayesha Siddiqua");
        text.ShouldContain("+8801712349999");
    }

    [Fact]
    public void It_goes_to_the_shop_and_not_to_the_customer()
    {
        var rendered = Render("Accepted");

        // The number in the body is the customer's; the one it is sent to is
        // the shop's. Conflating them puts a customer's number in the "sent to"
        // column of the admin screen — and texts the customer about themselves.
        rendered.RecipientPhone.ShouldBe(ShopMobile);
        rendered.RecipientPhone.ShouldNotBe("+8801712349999");
    }

    // -------------------------------------------------------------------------
    // No
    // -------------------------------------------------------------------------

    [Fact]
    public void A_refusal_is_not_shouted()
    {
        var text = Render("Declined", "Too expensive.").SmsText!;

        // Case.Sensitive, because the whole assertion is about the case: the
        // default comparison would find "DECLINED" inside "declined" and pass
        // whatever the message said.
        text.ShouldNotContain("DECLINED", Case.Sensitive);
        text.ShouldContain("declined", Case.Sensitive);
    }

    [Fact]
    public void And_carries_the_reason_they_gave()
    {
        // The one moment a customer ever says why. It was being written to a
        // column nobody reads.
        var text = Render("Declined", "Found it cheaper in Gulshan.").SmsText!;

        text.ShouldContain("Found it cheaper in Gulshan.");
    }

    [Fact]
    public void A_refusal_with_nothing_said_does_not_trail_an_empty_label()
    {
        var text = Render("Declined").SmsText!;

        text.ShouldNotContain("Reason:");
        text.ShouldContain("declined");
    }

    // -------------------------------------------------------------------------
    // What it costs
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("Accepted", null)]
    [InlineData("Declined", null)]
    [InlineData("Declined", "Too expensive.")]
    [InlineData("Declined", "Found the same thing cheaper at another shop in Gulshan.")]
    public void Every_answer_fits_in_one_billed_part(string answer, string? reason)
    {
        var text = Render(answer, reason).SmsText!;

        SmsParts.Count(text).ShouldBe(1, text);
    }

    [Fact]
    public void A_customer_who_writes_an_essay_does_not_cost_the_shop_four_messages()
    {
        var essay = string.Join(' ', Enumerable.Repeat("far too expensive for what it is", 40));

        var text = Render("Declined", essay).SmsText!;

        SmsParts.Count(text).ShouldBe(1, text);

        // Cut, not dropped: what room there was still says something.
        text.ShouldContain("Reason: far too expensive");
    }

    [Fact]
    public void The_things_that_must_be_there_survive_the_cutting()
    {
        var essay = string.Join(' ', Enumerable.Repeat("no", 200));

        var text = Render("Declined", essay).SmsText!;

        // The reason is what gives way, never the number, the money or somebody
        // to ring.
        text.ShouldContain("WHQ-2609-00008");
        text.ShouldContain("BDT 245,000");
        text.ShouldContain("+8801712349999");
    }

    [Fact]
    public void A_reason_too_short_to_survive_the_cut_is_left_to_the_email()
    {
        // One enormous unbroken word: there is no useful fragment of it, so
        // spending characters on a piece of one would buy the reader nothing.
        var rendered = Render("Declined", new string('x', 400));

        SmsParts.Count(rendered.SmsText).ShouldBe(1, rendered.SmsText);
        rendered.EmailHtml!.ShouldContain("xxxx");
    }

    // -------------------------------------------------------------------------
    // The email
    // -------------------------------------------------------------------------

    [Fact]
    public void The_email_carries_the_reason_whole()
    {
        var essay = string.Join(' ', Enumerable.Repeat("far too expensive for what it is", 40));

        var rendered = Render("Declined", essay);

        rendered.EmailHtml!.ShouldContain(essay);
        rendered.EmailSubject!.ShouldContain("WHQ-2609-00008");
    }

    [Fact]
    public void And_tells_the_shop_what_to_do_next_when_the_answer_was_yes()
    {
        var rendered = Render("Accepted");

        var html = rendered.EmailHtml!;

        html.ShouldContain("Turn it into an order");

        // Addressed to the shop, so it does not close by telling the shop to
        // ring itself.
        html.ShouldContain("Call the customer on");
        html.ShouldNotContain(ShopPhone);
    }

    [Fact]
    public void A_reason_containing_markup_cannot_break_the_email()
    {
        var rendered = Render("Declined", "<script>alert(1)</script> too dear");

        var html = rendered.EmailHtml!;

        html.ShouldNotContain("<script>");
        html.ShouldContain("&lt;script&gt;");
    }
}
