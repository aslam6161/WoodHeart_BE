using Shouldly;
using WoodHeart.Service.Services.Notifications;

namespace WoodHeart.Tests.Notifications;

/// <summary>
/// The samples the admin screen previews from.
/// </summary>
/// <remarks>
/// <para>
/// These exist to catch the one way a preview can lie. The sample payloads are
/// written here by hand and the real ones are built by the services that
/// enqueue them, so a field renamed on one side and not the other leaves the
/// message rendering with a hole in it — "Order  confirmed." — and a shop
/// deciding whether that message is worth paying for would be reading a
/// slightly wrong one.
/// </para>
/// <para>
/// So every sample is rendered and checked for the thing it is about. The
/// message cannot be compared to a fixed string, because the wording is meant
/// to be edited; what it can be held to is that the number it exists to carry
/// actually reached it.
/// </para>
/// </remarks>
public class NotificationCatalogTests
{
    private const string ShopPhone = "01712345678";

    [Fact]
    public void Every_kind_of_message_the_worker_can_send_is_described()
    {
        // A message the code sends and the catalogue does not describe is one
        // nobody can find on the screen, and so one nobody can turn off.
        NotificationCatalog.All
            .Select(e => e.Code)
            .OrderBy(c => c)
            .ShouldBe(NotificationTemplates.KnownTypes.OrderBy(c => c));
    }

    [Fact]
    public void And_nothing_is_described_that_cannot_be_sent()
    {
        foreach (var entry in NotificationCatalog.All)
        {
            NotificationTemplates.KnownTypes.ShouldContain(entry.Code);
            entry.Name.ShouldNotBeNullOrWhiteSpace();
            entry.WhenItFires.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("bn")]
    public void Every_sample_renders_a_message_with_something_in_it(string language)
    {
        foreach (var entry in NotificationCatalog.All)
        {
            if (language == "bn" && !entry.SupportsBangla)
            {
                continue;
            }

            var sample = NotificationCatalog.Sample(entry.Code, language);

            sample.ShouldNotBeNull($"{entry.Code} has no sample to preview from.");

            var rendered = NotificationTemplates.Render(entry.Code, sample, ShopPhone);

            rendered.ShouldNotBeNull($"{entry.Code} rendered nothing from its own sample.");
            rendered.Value.SmsText.ShouldNotBeNullOrWhiteSpace(
                $"{entry.Code} rendered an empty SMS in {language}.");
        }
    }

    [Theory]
    [InlineData("order.placed", "WH-2609-00042")]
    [InlineData("order.status_changed", "WH-2609-00042")]
    [InlineData("booking.requested", "WHC-2609-00017")]
    [InlineData("booking.status_changed", "WHC-2609-00017")]
    [InlineData("booking.reminder", "WHC-2609-00017")]
    [InlineData("quotation.sent", "WHQ-2609-00008")]
    [InlineData("quotation.converted", "WH-2609-00043")]
    public void The_number_the_message_exists_to_carry_reaches_it(string code, string number)
    {
        // The drift check. A renamed payload field does not throw — it renders
        // as an empty string — so the only way to notice is to ask for the one
        // value the message is built around.
        var sample = NotificationCatalog.Sample(code, "en");

        var rendered = NotificationTemplates.Render(code, sample!, ShopPhone);

        rendered!.Value.SmsText!.ShouldContain(number);
    }

    [Fact]
    public void The_shop_digest_names_what_is_running_out()
    {
        var sample = NotificationCatalog.Sample("stock.low", "en");

        var rendered = NotificationTemplates.Render("stock.low", sample!, ShopPhone);

        rendered!.Value.SmsText!.ShouldContain("Teak dining table");
        rendered.Value.EmailHtml!.ShouldContain("TDT-6-NAT");
    }

    [Fact]
    public void A_Bangla_sample_is_the_same_message_in_the_other_language()
    {
        var english = NotificationTemplates.Render(
            "order.placed", NotificationCatalog.Sample("order.placed", "en")!, ShopPhone);

        var bangla = NotificationTemplates.Render(
            "order.placed", NotificationCatalog.Sample("order.placed", "bn")!, ShopPhone);

        // Same order, different words — which is what makes the two part counts
        // beside each other on the screen a comparison rather than a coincidence.
        english!.Value.SmsText!.ShouldContain("WH-2609-00042");
        bangla!.Value.SmsText!.ShouldContain("WH-2609-00042");
        bangla.Value.SmsText.ShouldNotBe(english.Value.SmsText);
    }

    // -------------------------------------------------------------------------
    // Reading an outbox row back
    // -------------------------------------------------------------------------

    [Fact]
    public void The_reference_comes_out_of_the_idempotency_key()
    {
        // Every service builds its key from the number the message concerns, so
        // the number is in an indexed column rather than only inside jsonb.
        NotificationCatalog.Reference("order.placed:WH-2609-00042", null)
            .ShouldBe("WH-2609-00042");

        NotificationCatalog.Reference("booking.status:WHC-2609-00017:Confirmed:2026-09-24T10:00:00Z", null)
            .ShouldBe("WHC-2609-00017");
    }

    [Fact]
    public void A_row_with_no_key_falls_back_to_its_payload()
    {
        NotificationCatalog.Reference(null, """{"quotationNumber":"WHQ-2609-00008"}""")
            .ShouldBe("WHQ-2609-00008");
    }

    [Fact]
    public void A_payload_that_will_not_parse_renders_without_a_reference_rather_than_throwing()
    {
        // A row like this is exactly what somebody has opened the screen to
        // find. It has to draw.
        Should.NotThrow(() => NotificationCatalog.Reference(null, "not json at all"));

        NotificationCatalog.Reference(null, "not json at all").ShouldBeNull();
        NotificationCatalog.MaskedRecipient("not json at all").ShouldBeNull();
    }

    [Fact]
    public void The_recipient_is_never_printed_in_full()
    {
        var masked = NotificationCatalog.MaskedRecipient("""{"contactPhone":"+8801712349999"}""");

        masked.ShouldNotBeNull();
        masked.ShouldNotContain("1712349999");
    }
}
