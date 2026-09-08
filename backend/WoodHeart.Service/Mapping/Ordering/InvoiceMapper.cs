using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Service.DTOs.Ordering;

namespace WoodHeart.Service.Mapping.Ordering;

/// <summary>
/// A placed order to the model the invoice is drawn from.
/// </summary>
/// <remarks>
/// <para>
/// Separated from <c>InvoiceDocument</c>'s drawing code so that what the
/// invoice <em>says</em> can be asserted without producing a PDF and reading it
/// back. The figures on an invoice are the part worth testing; the millimetres
/// are not.
/// </para>
/// <para>
/// <b>Nothing is recomputed.</b> Every amount is copied straight off the order,
/// including the ones that look derivable. Delivery in particular: staff can
/// override it, so a total re-derived from the lines would disagree with the
/// order on exactly the invoices where somebody had already made a decision.
/// </para>
/// </remarks>
public static class InvoiceMapper
{
    public static InvoiceModel ToModel(
        Order order,
        InvoiceShop shop,
        DateTimeOffset issuedAt,
        DateTimeOffset printedAt,
        string paymentMethodName) =>
        new()
        {
            Shop = shop,
            InvoiceNumber = order.OrderNumber,
            IssuedAt = issuedAt,
            PrintedAt = printedAt,
            Currency = order.Currency,
            DeliveryNote = order.DeliveryNote,

            Customer = new InvoiceCustomer
            {
                Name = order.ContactName,
                Phone = order.ContactPhone,
                Email = order.ContactEmail,
                AddressLines = AddressLines(order.ShippingAddress)
            },

            // Ordered by id, which is the order they were added to the basket.
            // Anything else — alphabetical, by price — would make an invoice
            // hard to check against the packing list beside it.
            Lines =
            [
                .. order.Lines
                    .OrderBy(line => line.Id)
                    .Select(line => new InvoiceLine
                    {
                        Description = line.ProductNameEn,
                        VariantName = line.VariantName,
                        Sku = line.Sku,
                        Quantity = line.Quantity,
                        UnitPrice = line.UnitPrice.Amount,
                        DiscountAmount = line.DiscountAmount.Amount,
                        LineTotal = line.LineTotal.Amount
                    })
            ],

            Totals = new InvoiceTotals
            {
                Subtotal = order.Subtotal.Amount,
                DiscountTotal = order.DiscountTotal.Amount,
                GoodsNet = order.GoodsNet.Amount,
                VatAmount = order.VatAmount.Amount,
                VatRatePercent = order.VatRatePercent,
                PricesIncludeVat = order.PricesIncludeVat,
                DeliveryFee = order.DeliveryFee.Amount,
                DeliveryWaived = order.DeliveryWaived,
                PaymentSurcharge = order.PaymentSurcharge.Amount,
                GrandTotal = order.GrandTotal.Amount
            },

            Payment = new InvoicePayment
            {
                MethodName = paymentMethodName,
                StatusName = StatusName(order.PaymentStatus),
                AmountDue = AmountDue(order)
            }
        };

    /// <summary>
    /// The address, broadest last, one component per line.
    /// </summary>
    /// <remarks>
    /// The opposite arrangement to <see cref="DeliveryAddress.ToSingleLine"/>'s
    /// comma list, which exists to fit inside an SMS. Here there is a whole
    /// sheet of A4, and an address a rider reads off paper wants the house on
    /// its own line and the landmark under it.
    /// </remarks>
    public static IReadOnlyList<string> AddressLines(DeliveryAddress address)
    {
        var lines = new List<string> { address.AddressLine };

        if (!string.IsNullOrWhiteSpace(address.Landmark))
        {
            lines.Add(address.Landmark);
        }

        AddJoined(lines, address.Area, address.Upazila);

        var region = Join(address.District, address.Division);

        lines.Add(string.IsNullOrWhiteSpace(address.Postcode)
            ? region
            : $"{region} – {address.Postcode}");

        return lines;
    }

    /// <summary>
    /// What is still owed, or null.
    /// </summary>
    /// <remarks>
    /// Only when nothing at all has been collected. The order records that an
    /// advance was taken but not how much, so any figure printed for a
    /// part-paid order would be a guess — and a guess in an "amount due" box is
    /// a guess a rider collects.
    /// </remarks>
    private static decimal? AmountDue(Order order) =>
        order.PaymentStatus is PaymentStatus.Unpaid or PaymentStatus.Failed
            ? order.GrandTotal.Amount
            : null;

    private static string StatusName(PaymentStatus status) => status switch
    {
        PaymentStatus.Unpaid => "Unpaid",
        PaymentStatus.AdvancePaid => "Advance received",
        PaymentStatus.Paid => "Paid in full",
        PaymentStatus.PartiallyRefunded => "Partially refunded",
        PaymentStatus.Refunded => "Refunded",
        PaymentStatus.Failed => "Payment failed",
        _ => status.ToString()
    };

    private static void AddJoined(List<string> lines, string? first, string? second)
    {
        var joined = Join(first, second);

        if (joined.Length > 0)
        {
            lines.Add(joined);
        }
    }

    private static string Join(string? first, string? second) =>
        string.Join(", ", new[] { first, second }.Where(p => !string.IsNullOrWhiteSpace(p)));
}
