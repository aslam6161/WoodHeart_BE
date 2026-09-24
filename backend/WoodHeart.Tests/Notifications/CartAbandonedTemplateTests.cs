using System.Text.Json;
using WoodHeart.Service.Infrastructure.Notifications;
using WoodHeart.Service.Services.Notifications;

namespace WoodHeart.Tests.Notifications;

/// <summary>
/// The message that says a basket is still here.
/// </summary>
/// <remarks>
/// <para>
/// <b>It names no money, and that is the decision worth defending.</b> A
/// basket's prices are a snapshot taken as each line went in, and the basket
/// re-prices itself when the customer opens it. Quoting the snapshot hours
/// later risks naming a figure the shop will not honour — and being corrected
/// on the doorstep is worse than never having said it. The email lists what is
/// in the basket and says plainly that prices are settled at checkout.
/// </para>
/// <para>
/// <b>It names the thing rather than the count.</b> "2 items" is a receipt;
/// "Segun king bed with storage" is the object somebody stood in a showroom
/// thinking about. A product name has no length anybody controls, so it is the
/// field that gives way when the message will not fit.
/// </para>
/// </remarks>
public class CartAbandonedTemplateTests
{
    private const string Type = "cart.abandoned";

    private const string ShopPhone = "01712345678";

    private static RenderedNotification Render(
        string firstItem = "Segun king bed with storage",
        int otherItems = 2,
        string language = "en") =>
        NotificationTemplates.Render(
            Type,
            JsonSerializer.Serialize(new
            {
                contactName = "Ayesha Siddiqua",
                contactPhone = "+8801712349999",
                contactEmail = "ayesha@example.com",
                language,
                firstItem,
                otherItems,
                items = new[]
                {
                    new { name = firstItem, quantity = 1 },
                    new { name = "Bedside table, pair", quantity = 1 },
                    new { name = "Segun wardrobe, 3 door", quantity = 1 }
                }
            }),
            ShopPhone)!.Value;

    // -------------------------------------------------------------------------
    // What it says
    // -------------------------------------------------------------------------

    [Fact]
    public void It_names_the_thing_they_were_thinking_about()
    {
        var text = Render().SmsText!;

        text.ShouldContain("Segun king bed");
        text.ShouldContain("2 more");
    }

    [Fact]
    public void A_basket_with_one_thing_in_it_does_not_say_and_0_more()
    {
        var text = Render(otherItems: 0).SmsText!;

        text.ShouldNotContain("0 more");
        text.ShouldContain("Segun king bed");
    }

    [Fact]
    public void It_asks_for_a_telephone_call()
    {
        // Half of what this shop sells is finished on the telephone, and
        // somebody sitting on a decision is likelier to ring than to go
        // hunting for the basket again.
        var text = Render().SmsText!;

        text.ShouldContain(ShopPhone);
    }

    [Fact]
    public void And_it_quotes_no_figure_at_all()
    {
        // The prices on a basket are a snapshot. Naming one hours later is
        // promising a number the shop may not stand behind.
        var text = Render().SmsText!;

        text.ShouldNotContain("BDT");
    }

    [Fact]
    public void It_goes_to_the_customer_who_left_it()
    {
        var rendered = Render();

        rendered.RecipientPhone.ShouldBe("+8801712349999");
        rendered.RecipientEmail.ShouldBe("ayesha@example.com");
    }

    // -------------------------------------------------------------------------
    // What it costs
    // -------------------------------------------------------------------------

    [Fact]
    public void It_fits_in_one_English_part()
    {
        var text = Render().SmsText!;

        SmsParts.Count(text).ShouldBe(1, text);
    }

    [Fact]
    public void A_product_named_by_somebody_with_a_lot_to_say_still_fits()
    {
        var mouthful = string.Join(
            ' ', Enumerable.Repeat("hand finished Segun hardwood king bed with storage drawers", 6));

        var text = Render(firstItem: mouthful).SmsText!;

        SmsParts.Count(text).ShouldBe(1, text);

        // Cut, not dropped: the reader still learns which thing it was.
        text.ShouldContain("hand finished Segun");

        // And never at the cost of the number they are being asked to ring.
        text.ShouldContain(ShopPhone);
        text.ShouldContain("2 more");
    }

    [Fact]
    public void The_Bangla_form_stays_within_two_parts()
    {
        // Unicode, so a part is 70 characters rather than 160. Two is the most
        // this message is worth.
        var text = Render(language: "bn").SmsText!;

        SmsParts.Count(text).ShouldBeLessThanOrEqualTo(2, text);
        text.ShouldContain(ShopPhone);
    }

    [Fact]
    public void And_says_something_different_from_the_English_one()
    {
        Render(language: "bn").SmsText.ShouldNotBe(Render().SmsText);
    }

    // -------------------------------------------------------------------------
    // The email
    // -------------------------------------------------------------------------

    [Fact]
    public void The_email_lists_the_whole_basket()
    {
        var html = Render().EmailHtml!;

        html.ShouldContain("Segun king bed with storage");
        html.ShouldContain("Bedside table, pair");
        html.ShouldContain("Segun wardrobe, 3 door");
    }

    [Fact]
    public void And_says_where_the_price_is_settled()
    {
        // The one thing the SMS deliberately leaves out has to be said
        // somewhere, or the omission becomes a surprise instead of a caution.
        var html = Render().EmailHtml!;

        html.ShouldContain("settled at checkout");
    }

    [Fact]
    public void A_product_name_with_markup_in_it_cannot_break_the_email()
    {
        var rendered = NotificationTemplates.Render(
            Type,
            JsonSerializer.Serialize(new
            {
                contactName = "Ayesha Siddiqua",
                contactPhone = "+8801712349999",
                contactEmail = "ayesha@example.com",
                language = "en",
                firstItem = "Bed",
                otherItems = 0,
                items = new[] { new { name = "<script>alert(1)</script>", quantity = 1 } }
            }),
            ShopPhone)!.Value;

        var html = rendered.EmailHtml!;

        html.ShouldNotContain("<script>");
        html.ShouldContain("&lt;script&gt;");
    }
}
