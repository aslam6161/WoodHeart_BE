using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.Entity.Consultations;
using WoodHeart.Domain.Entity.Identity;
using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.Enums.Quotations;
using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Domain.Entity.Quotations;

/// <summary>
/// What the shop offered to do, for how much, and until when.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the funnel.</b> A consultation on its own is an hour of somebody's
/// time sold at cost; the money is in what the designer proposes afterwards.
/// A quotation is that proposal, written down, priced, and convertible into an
/// order in one press — which is why <see cref="BookingId"/> is here and why
/// consultations and ordering share one customer identity.
/// </para>
/// <para>
/// <b>Every figure is frozen, exactly as an order's is.</b> A quotation is a
/// promise of a price, and the promise has to survive the catalogue changing
/// underneath it. That is also why converting one does not re-price: honouring
/// the quote is the whole point of having given it.
/// </para>
/// <para>
/// <b>A line need not be something in the catalogue.</b> The most valuable
/// thing a designer quotes is often a wardrobe built to the shape of a
/// particular alcove, which has no variant and never will. Those lines carry
/// their own description and price and simply hold no stock — see
/// <see cref="QuotationLine.ProductVariantId"/>.
/// </para>
/// <para>
/// <b>The customer sees it by number and phone</b>, like a guest order and a
/// guest booking: the same two facts, for the same reason.
/// </para>
/// </remarks>
public class Quotation : BaseEntity
{
    /// <summary>The number quoted down the phone: <c>WHQ-2609-00042</c>.</summary>
    public string QuotationNumber { get; set; } = null!;

    /// <summary>
    /// The consultation this came out of, when it came out of one.
    /// </summary>
    /// <remarks>
    /// Nullable because the shop also quotes people who telephone. It is what
    /// makes "what did consultations earn us" answerable — without it, the
    /// second revenue line can only be measured by the hours it sold rather
    /// than the furniture it sold.
    /// </remarks>
    public long? BookingId { get; set; }

    public Booking? Booking { get; set; }

    /// <summary>Set when the customer had an account. Null for a guest.</summary>
    public long? CustomerId { get; set; }

    public AppUser? Customer { get; set; }

    // --- Contact, snapshotted -----------------------------------------------

    public string ContactName { get; set; } = null!;

    /// <summary>E.164, normalised — <c>+8801712345678</c>.</summary>
    public string ContactPhone { get; set; } = null!;

    public string? ContactEmail { get; set; }

    public string CustomerLanguage { get; set; } = GlobalConstants.DefaultLanguage;

    // --- Where it is going ----------------------------------------------------

    /// <summary>
    /// Null while the designer is still working it up.
    /// </summary>
    /// <remarks>
    /// Delivery cannot be priced without it, so a quotation with no address
    /// carries no delivery charge and says so. It has to be there before the
    /// quotation can become an order.
    /// </remarks>
    public DeliveryAddress? ShippingAddress { get; set; }

    /// <summary>The zone as it was when the quotation was priced.</summary>
    public DeliveryZone DeliveryZone { get; set; }

    // --- Money, all frozen when it is sent -------------------------------------

    public string Currency { get; set; } = Money.Bdt;

    /// <summary>Goods before any discount.</summary>
    public Money Subtotal { get; set; } = null!;

    /// <summary>
    /// What the designer took off, as a figure.
    /// </summary>
    /// <remarks>
    /// Typed by hand rather than resolved by the discount engine. A quotation
    /// discount is a negotiation — "we will do the pair for sixty" — not a
    /// campaign, and running it through the engine would mean inventing a
    /// discount record for every conversation.
    /// </remarks>
    public Money DiscountTotal { get; set; } = null!;

    /// <summary>Goods excluding VAT, after discount.</summary>
    public Money GoodsNet { get; set; } = null!;

    public Money VatAmount { get; set; } = null!;

    /// <summary>The rate that applied. Copied, so an old quotation stays true.</summary>
    public decimal VatRatePercent { get; set; }

    public bool PricesIncludeVat { get; set; }

    public Money DeliveryFee { get; set; } = null!;

    /// <summary>Set when the designer priced delivery by hand.</summary>
    public Money? DeliveryFeeOverride { get; set; }

    /// <summary>
    /// Goods, VAT and delivery. <b>Not</b> any payment charge.
    /// </summary>
    /// <remarks>
    /// A payment surcharge belongs to the way the customer chooses to pay,
    /// which is decided when the order is placed rather than when the quote is
    /// written. The quotation says so in as many words, so the figure on the
    /// order is never a surprise.
    /// </remarks>
    public Money GrandTotal { get; set; } = null!;

    // --- Its life ---------------------------------------------------------------

    public QuotationStatus Status { get; set; } = QuotationStatus.Draft;

    /// <summary>
    /// The last day the figures stand.
    /// </summary>
    /// <remarks>
    /// A Dhaka date rather than an instant: "good until the 15th" means the
    /// whole of the 15th as the shop lives it, not until six in the evening.
    /// </remarks>
    public DateOnly ValidUntil { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    /// <summary>When the customer accepted or declined.</summary>
    public DateTimeOffset? RespondedAt { get; set; }

    /// <summary>Why they said no, when they said why.</summary>
    public string? DeclineReason { get; set; }

    /// <summary>What the customer reads: terms, lead times, what is not included.</summary>
    public string? Notes { get; set; }

    /// <summary>Staff-only. Never shown to the customer.</summary>
    public string? InternalNotes { get; set; }

    /// <summary>The order it became, once it became one.</summary>
    public long? ConvertedOrderId { get; set; }

    public Order? ConvertedOrder { get; set; }

    public ICollection<QuotationLine> Lines { get; set; } = [];

    /// <summary>Whether the customer may still act on it themselves.</summary>
    public bool IsOpen => Status == QuotationStatus.Sent;
}

/// <summary>
/// One thing quoted for.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="ProductVariantId"/> is nullable, and that is the interesting
/// part.</b> A catalogue line becomes an order line that holds stock; a
/// made-to-measure line — "wardrobe, 7ft, segun, to the alcove" — has no
/// variant, holds no stock, and is made to order. Both belong on the same
/// quotation because that is how the customer was quoted.
/// </para>
/// <para>
/// The description is stored rather than read through the variant, for the
/// same reason an order line's is: renaming a product next year must not
/// rewrite what somebody was quoted.
/// </para>
/// </remarks>
public class QuotationLine : BaseEntity
{
    public long QuotationId { get; set; }

    public Quotation Quotation { get; set; } = null!;

    /// <summary>Null for anything the shop will make rather than pick off a shelf.</summary>
    public long? ProductVariantId { get; set; }

    public ProductVariant? ProductVariant { get; set; }

    public long? ProductId { get; set; }

    /// <summary>What the customer reads on the line.</summary>
    public string Description { get; set; } = null!;

    /// <summary>"Segun · 6ft · Matte", when it came from the catalogue.</summary>
    public string? VariantName { get; set; }

    public string? Sku { get; set; }

    public string? ImagePath { get; set; }

    public int Quantity { get; set; }

    public Money UnitPrice { get; set; } = null!;

    /// <summary><see cref="UnitPrice"/> × <see cref="Quantity"/>.</summary>
    public Money LineTotal { get; set; } = null!;

    /// <summary>Working days quoted for something made to order.</summary>
    public int? LeadTimeDays { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Whether this line will come off a shelf when the order is placed.</summary>
    public bool HoldsStock => ProductVariantId is not null;
}
