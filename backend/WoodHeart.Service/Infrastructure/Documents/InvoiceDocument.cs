using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WoodHeart.Service.DTOs.Ordering;

namespace WoodHeart.Service.Infrastructure.Documents;

/// <summary>
/// The invoice, as it appears on paper.
/// </summary>
/// <remarks>
/// <para>
/// <b>Designed for a cheap laser printer.</b> No block colour, no reversed-out
/// white text, no logo — the page has to be legible from a shop printer that is
/// low on toner, and it goes in a box with a wardrobe rather than into a design
/// portfolio. Rules and weight do the separating that colour would.
/// </para>
/// <para>
/// <b>A4 rather than Letter.</b> It is the paper sold in Bangladesh, and a
/// document that reflows when it meets the office printer is a document with a
/// row of the table on a second page by itself.
/// </para>
/// <para>
/// Money is formatted with <see cref="CultureInfo.InvariantCulture"/> throughout.
/// A Bangla or European server locale would otherwise render 254,100.00 as
/// 254.100,00 — the same digits, an invoice off by a factor of a hundred to
/// anyone reading it in the other convention.
/// </para>
/// </remarks>
internal sealed class InvoiceDocument(InvoiceModel model) : IDocument
{
    private const string DateFormat = "dd MMM yyyy";
    private const string DateTimeFormat = "dd MMM yyyy, HH:mm";

    private static readonly TextStyle Label =
        TextStyle.Default.FontSize(7).FontColor(Colors.Grey.Darken1).Bold();

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Invoice {model.InvoiceNumber}",
        Author = model.Shop.Name,
        Subject = $"Invoice for order {model.InvoiceNumber}",

        // The creation date is the reprint's date, not the order's. A PDF that
        // claims to have been created the day the order was placed would be a
        // small lie told to anybody inspecting the file's properties.
        CreationDate = model.PrintedAt,
        ModifiedDate = model.PrintedAt
    };

    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public void Compose(IDocumentContainer container) =>
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1.4f, Unit.Centimetre);

            // The Bangla family is second, so it is reached only for characters
            // the Latin one cannot draw — which is exactly the customer's name
            // and address, and nothing the shop wrote itself.
            page.DefaultTextStyle(text => text
                .FontSize(9)
                .FontFamily(InvoiceRenderer.LatinFont, InvoiceRenderer.BanglaFont)
                .FontColor(Colors.Black));

            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });

    // -------------------------------------------------------------------------
    // Header
    // -------------------------------------------------------------------------

    private void ComposeHeader(IContainer container) =>
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(shop =>
                {
                    shop.Item().Text(model.Shop.Name).FontSize(16).Bold();

                    foreach (var line in ShopLines())
                    {
                        shop.Item().Text(line).FontSize(8).FontColor(Colors.Grey.Darken2);
                    }
                });

                row.ConstantItem(180).Column(meta =>
                {
                    meta.Item().AlignRight().Text("INVOICE")
                        .FontSize(16).Bold().FontColor(Colors.Grey.Darken2);

                    meta.Item().PaddingTop(6).AlignRight()
                        .Text(model.InvoiceNumber).FontSize(11).Bold();

                    meta.Item().AlignRight()
                        .Text(model.IssuedAt.ToString(DateFormat, CultureInfo.InvariantCulture))
                        .FontSize(8).FontColor(Colors.Grey.Darken2);
                });
            });

            column.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Darken1);
        });

    /// <summary>
    /// The shop's contact lines, skipping the ones that are not set.
    /// </summary>
    /// <remarks>
    /// A blank line where the BIN should be reads as a registered business that
    /// forgot to fill the field in. Absent is honest; empty is not.
    /// </remarks>
    private IEnumerable<string> ShopLines()
    {
        if (!string.IsNullOrWhiteSpace(model.Shop.AddressLine))
        {
            yield return model.Shop.AddressLine;
        }

        var contact = new[] { model.Shop.Phone, model.Shop.Email }
            .Where(part => !string.IsNullOrWhiteSpace(part));

        var joined = string.Join("  ·  ", contact);

        if (joined.Length > 0)
        {
            yield return joined;
        }

        if (!string.IsNullOrWhiteSpace(model.Shop.Bin))
        {
            yield return $"BIN {model.Shop.Bin}";
        }
    }

    // -------------------------------------------------------------------------
    // Content
    // -------------------------------------------------------------------------

    private void ComposeContent(IContainer container) =>
        container.PaddingVertical(12).Column(column =>
        {
            column.Spacing(14);

            column.Item().Element(ComposeParties);
            column.Item().Element(ComposeLines);
            column.Item().Element(ComposeSummary);

            if (!string.IsNullOrWhiteSpace(model.DeliveryNote))
            {
                column.Item().Element(ComposeDeliveryNote);
            }
        });

    private void ComposeParties(IContainer container) =>
        container.Row(row =>
        {
            row.RelativeItem().Column(billed =>
            {
                billed.Item().Text("BILL TO").Style(Label);
                billed.Item().PaddingTop(3).Text(model.Customer.Name).Bold();
                billed.Item().Text(model.Customer.Phone);

                if (!string.IsNullOrWhiteSpace(model.Customer.Email))
                {
                    billed.Item().Text(model.Customer.Email);
                }
            });

            row.ConstantItem(14);

            row.RelativeItem().Column(shipped =>
            {
                shipped.Item().Text("DELIVER TO").Style(Label);

                foreach (var line in model.Customer.AddressLines)
                {
                    shipped.Item().PaddingTop(1).Text(line);
                }
            });

            row.ConstantItem(14);

            row.ConstantItem(130).Column(payment =>
            {
                payment.Item().Text("PAYMENT").Style(Label);
                payment.Item().PaddingTop(3).Text(model.Payment.MethodName);
                payment.Item().Text(model.Payment.StatusName)
                    .FontColor(Colors.Grey.Darken2);
            });
        });

    // -------------------------------------------------------------------------
    // The table
    // -------------------------------------------------------------------------

    private void ComposeLines(IContainer container) =>
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(22);   // #
                columns.RelativeColumn();     // description
                columns.ConstantColumn(34);   // qty
                columns.ConstantColumn(72);   // unit price
                columns.ConstantColumn(62);   // discount
                columns.ConstantColumn(78);   // line total
            });

            // Repeated on every page, because a second sheet whose columns are
            // unlabelled is a sheet nobody can check the arithmetic on.
            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("#");
                header.Cell().Element(HeaderCell).Text("Item");
                header.Cell().Element(HeaderCell).AlignRight().Text("Qty");
                header.Cell().Element(HeaderCell).AlignRight().Text("Unit price");
                header.Cell().Element(HeaderCell).AlignRight().Text("Discount");
                header.Cell().Element(HeaderCell).AlignRight().Text("Amount");
            });

            var index = 1;

            foreach (var line in model.Lines)
            {
                table.Cell().Element(BodyCell).Text(
                    index.ToString(CultureInfo.InvariantCulture));

                table.Cell().Element(BodyCell).Column(description =>
                {
                    description.Item().Text(line.Description);

                    var detail = new[] { line.VariantName, $"SKU {line.Sku}" }
                        .Where(part => !string.IsNullOrWhiteSpace(part));

                    description.Item().Text(string.Join("  ·  ", detail))
                        .FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                });

                table.Cell().Element(BodyCell).AlignRight().Text(
                    line.Quantity.ToString(CultureInfo.InvariantCulture));

                table.Cell().Element(BodyCell).AlignRight().Text(Amount(line.UnitPrice));

                table.Cell().Element(BodyCell).AlignRight().Text(
                    line.DiscountAmount == 0m ? "—" : $"−{Amount(line.DiscountAmount)}");

                table.Cell().Element(BodyCell).AlignRight().Text(Amount(line.LineTotal));

                index++;
            }
        });

    private static IContainer HeaderCell(IContainer container) =>
        container
            .BorderBottom(1)
            .BorderColor(Colors.Grey.Darken1)
            .PaddingVertical(4)
            .PaddingHorizontal(3)
            .DefaultTextStyle(text => text.Bold().FontSize(8));

    private static IContainer BodyCell(IContainer container) =>
        container
            .BorderBottom(0.5f)
            .BorderColor(Colors.Grey.Lighten1)
            .PaddingVertical(5)
            .PaddingHorizontal(3);

    // -------------------------------------------------------------------------
    // Totals
    // -------------------------------------------------------------------------

    private void ComposeSummary(IContainer container) =>
        container.Row(row =>
        {
            row.RelativeItem();

            row.ConstantItem(250).Column(totals =>
            {
                totals.Item().Text($"All amounts in {model.Currency}")
                    .FontSize(7.5f).FontColor(Colors.Grey.Darken1);

                totals.Item().PaddingTop(4);

                var inclusive = model.Totals.PricesIncludeVat;

                Line(totals, inclusive ? "Subtotal (incl. VAT)" : "Subtotal",
                    model.Totals.Subtotal);

                if (model.Totals.DiscountTotal != 0m)
                {
                    Line(totals, "Discount", -model.Totals.DiscountTotal);
                }

                // Under inclusive pricing the VAT is already inside the
                // subtotal above, so it is not a row in this column — printing
                // it as one gives a column that does not add up to its own
                // total, and a customer who adds it themselves gets a bigger
                // number than the one they are being asked to pay. It goes
                // below the rule instead, as a memorandum.
                if (!inclusive)
                {
                    Line(totals, VatLabel(), model.Totals.VatAmount);
                }

                Line(totals, DeliveryLabel(), model.Totals.DeliveryFee);

                if (model.Totals.PaymentSurcharge != 0m)
                {
                    Line(totals, "Payment charge", model.Totals.PaymentSurcharge);
                }

                totals.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Grey.Darken1);

                totals.Item().PaddingTop(4).Row(grand =>
                {
                    grand.RelativeItem().Text("Total").Bold().FontSize(11);
                    grand.ConstantItem(110).AlignRight()
                        .Text(Amount(model.Totals.GrandTotal)).Bold().FontSize(11);
                });

                if (inclusive)
                {
                    totals.Item().PaddingTop(2).Row(vat =>
                    {
                        vat.RelativeItem().Text(VatLabel())
                            .FontSize(8).FontColor(Colors.Grey.Darken1);
                        vat.ConstantItem(110).AlignRight()
                            .Text(Amount(model.Totals.VatAmount))
                            .FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                }

                if (model.Payment.AmountDue is { } due)
                {
                    totals.Item().PaddingTop(8)
                        .Background(Colors.Grey.Lighten3)
                        .Padding(6)
                        .Row(amountDue =>
                        {
                            amountDue.RelativeItem().Text("Amount due").Bold();
                            amountDue.ConstantItem(110).AlignRight().Text(Amount(due)).Bold();
                        });
                }
            });
        });

    private static void Line(ColumnDescriptor column, string label, decimal amount) =>
        column.Item().PaddingVertical(1).Row(row =>
        {
            row.RelativeItem().Text(label);
            row.ConstantItem(110).AlignRight().Text(Amount(amount));
        });

    /// <summary>
    /// The VAT row's label, carrying the rate that was actually charged.
    /// </summary>
    /// <remarks>
    /// The rate is snapshotted on the order rather than read from settings, so
    /// an invoice reprinted after the rate changes still says what the customer
    /// paid. Printing it here is what makes that visible on the paper.
    /// </remarks>
    private string VatLabel()
    {
        var rate = model.Totals.VatRatePercent.ToString("0.##", CultureInfo.InvariantCulture);

        return model.Totals.PricesIncludeVat
            ? $"Includes VAT at {rate}%"
            : $"VAT {rate}%";
    }

    private string DeliveryLabel() =>
        model.Totals.DeliveryWaived && model.Totals.DeliveryFee == 0m
            ? "Delivery (free)"
            : "Delivery";

    private void ComposeDeliveryNote(IContainer container) =>
        container.Column(note =>
        {
            note.Item().Text("DELIVERY NOTE").Style(Label);
            note.Item().PaddingTop(2).Text(model.DeliveryNote!);
        });

    // -------------------------------------------------------------------------
    // Footer
    // -------------------------------------------------------------------------

    private void ComposeFooter(IContainer container) =>
        container.PaddingTop(8).Column(column =>
        {
            column.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);

            column.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem()
                    .Text($"Printed {model.PrintedAt.ToString(DateTimeFormat, CultureInfo.InvariantCulture)}")
                    .FontSize(7).FontColor(Colors.Grey.Darken1);

                // Numbered "1 of 3" rather than "1", so a customer holding one
                // sheet knows whether they are holding all of them.
                row.ConstantItem(120).AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(style => style
                        .FontSize(7).FontColor(Colors.Grey.Darken1));

                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        });

    /// <summary>
    /// Two decimals, grouped, culture-independent.
    /// </summary>
    /// <remarks>
    /// Two rather than the SMS templates' none. A text message pays by the
    /// character and rounds; an invoice is the document the shop's books
    /// reconcile against, and 17,616.28 of VAT is not 17,616.
    /// </remarks>
    private static string Amount(decimal value) =>
        value.ToString("N2", CultureInfo.InvariantCulture);
}
