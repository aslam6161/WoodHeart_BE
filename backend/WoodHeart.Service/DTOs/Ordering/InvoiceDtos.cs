namespace WoodHeart.Service.DTOs.Ordering;

/// <summary>
/// Everything that appears on an invoice, and nothing that does not.
/// </summary>
/// <remarks>
/// <para>
/// <b>The renderer is given this and no repository.</b> Drawing a page is not
/// the place to discover that a lazy-loaded collection needs a database, and a
/// document built from a plain object can be laid out in a test without one.
/// </para>
/// <para>
/// <b>Figures arrive already decided.</b> Nothing here is computed while
/// drawing — no summing of lines to reach a total, no re-deriving VAT from a
/// rate. Those figures were settled when the order was placed and frozen onto
/// it; recomputing them here would mean an invoice that disagrees with the
/// order it is an invoice for, and the arithmetic would be wrong first in the
/// one case that matters, where staff overrode the delivery charge.
/// </para>
/// </remarks>
public class InvoiceModel
{
    public required InvoiceShop Shop { get; init; }

    /// <summary>The order number. An invoice does not get a number of its own.</summary>
    /// <remarks>
    /// One order, one invoice, one number to quote down the phone. A separate
    /// sequence would mean staff holding two numbers for the same thing and
    /// customers reading out whichever one is nearer the top of the page.
    /// </remarks>
    public required string InvoiceNumber { get; init; }

    /// <summary>When the order was placed, in Dhaka time.</summary>
    public required DateTimeOffset IssuedAt { get; init; }

    /// <summary>When the document was drawn, in Dhaka time.</summary>
    /// <remarks>
    /// Printed in the footer beside the page number, and different from
    /// <see cref="IssuedAt"/> on every reprint. Which is the point: a reprint
    /// pulled during a dispute should say when it was pulled.
    /// </remarks>
    public required DateTimeOffset PrintedAt { get; init; }

    public required InvoiceCustomer Customer { get; init; }

    public required IReadOnlyList<InvoiceLine> Lines { get; init; }

    public required InvoiceTotals Totals { get; init; }

    public required InvoicePayment Payment { get; init; }

    /// <summary>What the customer asked for. Blank on most orders.</summary>
    public string? DeliveryNote { get; init; }

    /// <summary>ISO 4217, printed once in the totals heading rather than per row.</summary>
    public required string Currency { get; init; }
}

/// <summary>The shop's own details, read from store settings at render time.</summary>
/// <remarks>
/// Not snapshotted onto the order, unlike everything else on the page. A shop
/// that moves premises wants its current address on a reprint — the customer is
/// being told where to send a return, not where the shop used to be.
/// </remarks>
public class InvoiceShop
{
    public required string Name { get; init; }

    public string? AddressLine { get; init; }

    public string? Phone { get; init; }

    public string? Email { get; init; }

    /// <summary>Null when the shop is not VAT-registered. Then no BIN line is drawn.</summary>
    public string? Bin { get; init; }
}

/// <summary>Who the goods are going to, exactly as they were snapshotted.</summary>
public class InvoiceCustomer
{
    public required string Name { get; init; }

    public required string Phone { get; init; }

    public string? Email { get; init; }

    /// <summary>The delivery address, one component per line.</summary>
    public required IReadOnlyList<string> AddressLines { get; init; }
}

/// <summary>One row of the table.</summary>
public class InvoiceLine
{
    public required string Description { get; init; }

    /// <summary>"Segun · 6ft · Matte", drawn small under the description.</summary>
    public string? VariantName { get; init; }

    public required string Sku { get; init; }

    public required int Quantity { get; init; }

    public required decimal UnitPrice { get; init; }

    public required decimal DiscountAmount { get; init; }

    public required decimal LineTotal { get; init; }
}

/// <summary>The money block, in the order it is read.</summary>
public class InvoiceTotals
{
    public required decimal Subtotal { get; init; }

    public required decimal DiscountTotal { get; init; }

    public required decimal GoodsNet { get; init; }

    public required decimal VatAmount { get; init; }

    public required decimal VatRatePercent { get; init; }

    /// <summary>
    /// Whether the line prices above already contain the VAT figure.
    /// </summary>
    /// <remarks>
    /// Printed as words next to the VAT row, because the same set of numbers
    /// means two different things under the two regimes and the reader cannot
    /// tell which by looking.
    /// </remarks>
    public required bool PricesIncludeVat { get; init; }

    public required decimal DeliveryFee { get; init; }

    public required bool DeliveryWaived { get; init; }

    public required decimal PaymentSurcharge { get; init; }

    public required decimal GrandTotal { get; init; }
}

/// <summary>How this order is being paid for, and whether it has been.</summary>
public class InvoicePayment
{
    /// <summary>"Cash on delivery", "bKash" — the readable name, not the code.</summary>
    public required string MethodName { get; init; }

    /// <summary>"Unpaid", "Paid" — the status, spelled for a reader.</summary>
    public required string StatusName { get; init; }

    /// <summary>
    /// Set only when the whole amount is still outstanding.
    /// </summary>
    /// <remarks>
    /// Null once anything has been collected, including a part payment. The
    /// order does not record how much of an advance arrived, and a figure
    /// printed as "due" that is not what is due is worse than no figure: the
    /// rider would collect it.
    /// </remarks>
    public decimal? AmountDue { get; init; }
}

/// <summary>
/// A rendered invoice: the bytes, and what to call the file.
/// </summary>
/// <remarks>
/// The filename is decided here rather than in the controller because it is the
/// name that ends up in a folder of hundreds — <c>WH-2609-00042.pdf</c> sorts
/// and searches, <c>invoice.pdf (7)</c> does not.
/// </remarks>
public class InvoiceFile
{
    public const string Pdf = "application/pdf";

    public required byte[] Content { get; init; }

    public required string FileName { get; init; }
}
