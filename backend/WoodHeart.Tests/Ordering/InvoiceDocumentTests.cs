using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Service.DTOs.Ordering;
using WoodHeart.Service.Infrastructure.Documents;

namespace WoodHeart.Tests.Ordering;

/// <summary>
/// That the invoice actually draws.
/// </summary>
/// <remarks>
/// <para>
/// <b>These tests exist because the failure they catch is invisible.</b> A
/// missing glyph does not throw in production — it draws a placeholder box —
/// so a Bangla name that renders as a row of tofu would ship, print, and go out
/// in the box before anybody noticed. <see cref="InvoiceFixture"/> turns
/// QuestPDF's glyph check on for the whole assembly, which turns that silent
/// wrongness into a failing test.
/// </para>
/// <para>
/// The other failure worth a test is a layout exception: QuestPDF throws when
/// content cannot fit the space it was given, and the content here is typed by
/// customers and by whoever names the products.
/// </para>
/// </remarks>
public class InvoiceDocumentTests
{
    [Fact]
    public void An_invoice_renders_to_a_pdf()
    {
        var pdf = InvoiceRenderer.Render(InvoiceFixture.Model());

        pdf.ShouldNotBeEmpty();
        Encoding.ASCII.GetString(pdf, 0, 5).ShouldBe("%PDF-");
    }

    [Fact]
    public void A_Bangla_name_and_address_have_every_glyph_they_need()
    {
        // The whole reason two font families are embedded. The Latin family has
        // no Bengali glyphs at all, so without the fallback chain this renders
        // as boxes — and with the glyph check on, as a failure here instead.
        var order = InvoiceFixture.Order(
            contactName: "রাকিব হাসান",
            address: DeliveryAddress.Create(
                "ঢাকা", "ঢাকা", "বাড়ি ১২, রোড ৩",
                area: "ধানমন্ডি", landmark: "পপুলার ডায়াগনস্টিকের বিপরীতে"));

        Should.NotThrow(() => InvoiceRenderer.Render(InvoiceFixture.Model(order)));
    }

    [Fact]
    public void A_name_that_mixes_both_scripts_renders_too()
    {
        // Common in practice: a Bangla name with an English company after it.
        // Each run of text has to reach a different font within one line.
        var order = InvoiceFixture.Order(contactName: "রাকিব হাসান (Rakib Hasan)");

        Should.NotThrow(() => InvoiceRenderer.Render(InvoiceFixture.Model(order)));
    }

    [Fact]
    public void The_glyph_check_these_tests_rely_on_is_actually_on()
    {
        // Without this, the two tests above pass whether or not the Bengali
        // font is embedded — a missing glyph draws a placeholder box and the
        // render succeeds either way. This is what makes them mean something:
        // a character neither font can supply throws, so a silent gap in
        // coverage becomes a red test rather than tofu on the printed page.
        var order = InvoiceFixture.Order(contactName: "Rakib 🪑 Hasan");

        Should.Throw<Exception>(() => InvoiceRenderer.Render(InvoiceFixture.Model(order)));
    }

    [Fact]
    public void A_shop_that_has_configured_nothing_but_its_name_still_gets_an_invoice()
    {
        // The state the shop is in today: store.address and store.bin are not
        // set. An invoice must still print rather than throwing on a null, and
        // it must not print an empty "BIN:" line pretending otherwise.
        var bare = new InvoiceShop { Name = "WoodHeart" };

        Should.NotThrow(() => InvoiceRenderer.Render(InvoiceFixture.Model(shop: bare)));
    }

    [Fact]
    public void A_long_order_paginates_rather_than_overflowing()
    {
        // Forty lines will not fit one sheet. QuestPDF throws when content
        // cannot fit the space it was given, so this is the test that fails if
        // the table is ever put inside something of a fixed height.
        var lines = Enumerable.Range(1, 40)
            .Select(i => InvoiceFixture.Line(i, $"Dining Chair {i}", 2, 4_500m));

        var pages = new InvoiceDocument(InvoiceFixture.Model(InvoiceFixture.Order(lines: lines)))
            .GenerateImages(new ImageGenerationSettings { RasterDpi = 72 })
            .Count();

        pages.ShouldBeGreaterThan(1);
    }

    [Fact]
    public void A_product_name_nobody_would_choose_does_not_break_the_layout()
    {
        // Product names are typed into an admin form with no word breaks
        // enforced. A single unbroken token wider than its column is the
        // classic way a document generator starts throwing in production.
        var order = InvoiceFixture.Order(lines:
        [
            InvoiceFixture.Line(1, new string('W', 300), 1, 1_000m)
        ]);

        Should.NotThrow(() => InvoiceRenderer.Render(InvoiceFixture.Model(order)));
    }
}
