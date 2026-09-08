using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.Entity.Identity;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Domain.Entity.Ordering;

/// <summary>
/// A placed order: what was bought, by whom, for how much, and where it is.
/// </summary>
/// <remarks>
/// <para>
/// <b>An order is a snapshot, and the cart is not.</b> <c>Cart</c> stores no
/// totals because a basket's price must follow the catalogue; an order stores
/// every figure because its price must never move again. Editing a product's
/// price next month must not rewrite last month's invoices — that is not a
/// cosmetic concern, it is the difference between accounts that reconcile and
/// accounts that do not.
/// </para>
/// <para>
/// The same applies to the VAT rate and the inclusive flag: both are copied
/// onto the order, so an invoice reprinted in two years shows the rate that was
/// actually charged rather than today's.
/// </para>
/// <para>
/// <b>Three status axes, not one.</b> <see cref="Status"/> is where the work
/// is, <see cref="PaymentStatus"/> is where the money is, and
/// <see cref="FulfilmentStatus"/> is where the goods are. A cash-on-delivery
/// order is Confirmed and Unpaid for its whole life until the rider collects,
/// and a single status field cannot say that.
/// </para>
/// <para>
/// <b>A guest order is an ordinary order.</b> <see cref="CustomerId"/> is null
/// and the contact details stand on their own. Most furniture here is bought
/// without an account, so this is the main path — and because
/// <see cref="ContactPhone"/> is stored normalised, a guest who registers with
/// the same number later can be handed their history.
/// </para>
/// </remarks>
public class Order : BaseEntity
{
    /// <summary>
    /// The number a customer quotes down the phone: <c>WH-2609-00042</c>.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="BaseEntity.Id"/> on purpose. It is readable
    /// aloud, sorts by month, and does not tell a competitor how many orders
    /// were placed last week.
    /// </remarks>
    public string OrderNumber { get; set; } = null!;

    /// <summary>Set when the buyer had an account. Null for a guest order.</summary>
    public long? CustomerId { get; set; }

    public AppUser? Customer { get; set; }

    // --- Contact, snapshotted -----------------------------------------------

    /// <summary>
    /// Who to call about this order.
    /// </summary>
    /// <remarks>
    /// Stored on the order even for a signed-in customer, rather than read
    /// through <see cref="Customer"/>. Someone ordering a sofa delivered to a
    /// relative gives that person's name and number, and a customer who changes
    /// their own phone number next year must not silently change who the rider
    /// calls about a delivery that already happened.
    /// </remarks>
    public string ContactName { get; set; } = null!;

    /// <summary>E.164, normalised at placement — <c>+8801712345678</c>.</summary>
    /// <remarks>
    /// Normalised rather than stored as typed, because it is the key that links
    /// a guest order to an account created later. Four spellings of one number
    /// would mean four customers.
    /// </remarks>
    public string ContactPhone { get; set; } = null!;

    /// <summary>Optional. Many customers here have no email they read.</summary>
    public string? ContactEmail { get; set; }

    /// <summary>
    /// Which language to write to this customer in — <c>en</c> or <c>bn</c>.
    /// </summary>
    /// <remarks>
    /// Snapshotted for the same reason the VAT rate is. The confirmation is
    /// sent while the customer is still on the page, but the "on its way"
    /// message is sent days later by a member of staff whose own browser is in
    /// English — and without this the shipping SMS would arrive in a different
    /// language from the one that confirmed the order.
    /// </remarks>
    public string CustomerLanguage { get; set; } = GlobalConstants.DefaultLanguage;

    // --- Delivery ------------------------------------------------------------

    public DeliveryAddress ShippingAddress { get; set; } = null!;

    /// <summary>
    /// The zone as it was at placement, not derived from the address on read.
    /// </summary>
    /// <remarks>
    /// Redrawing the Dhaka boundary next year must not change what a customer
    /// was charged last year.
    /// </remarks>
    public DeliveryZone DeliveryZone { get; set; }

    /// <summary>What the customer asked for — a floor, a time, a gate code.</summary>
    public string? DeliveryNote { get; set; }

    // --- Money, all frozen at placement --------------------------------------

    public string Currency { get; set; } = Money.Bdt;

    /// <summary>Goods before any discount.</summary>
    public Money Subtotal { get; set; } = null!;

    public Money DiscountTotal { get; set; } = null!;

    /// <summary>Goods excluding VAT, after discount.</summary>
    public Money GoodsNet { get; set; } = null!;

    public Money VatAmount { get; set; } = null!;

    /// <summary>The rate that applied, e.g. <c>7.5</c>. Copied so an old invoice stays true.</summary>
    public decimal VatRatePercent { get; set; }

    /// <summary>Whether the line prices already contained the VAT above.</summary>
    /// <remarks>
    /// Without this an invoice cannot be reprinted correctly: the same figures
    /// mean different things under the two regimes, and the flag is a store
    /// setting that may be changed.
    /// </remarks>
    public bool PricesIncludeVat { get; set; }

    public Money DeliveryFee { get; set; } = null!;

    /// <summary>
    /// What the chosen payment method added, if anything.
    /// </summary>
    /// <remarks>
    /// A cash-handling charge on COD, or a gateway's percentage. Held as its own
    /// figure rather than folded into the delivery fee, because a customer who
    /// sees "delivery 500৳" on the cart page and "delivery 550৳" on the invoice
    /// has been misled — and because the shop needs to know what taking cash
    /// costs it.
    /// </remarks>
    public Money PaymentSurcharge { get; set; } = null!;

    /// <summary>
    /// <see cref="GoodsNet"/> + <see cref="VatAmount"/> + <see cref="DeliveryFee"/>
    /// + <see cref="PaymentSurcharge"/>, exactly.
    /// </summary>
    public Money GrandTotal { get; set; } = null!;

    /// <summary>True when the free-delivery threshold took the charge off.</summary>
    public bool DeliveryWaived { get; set; }

    /// <summary>
    /// True when staff set the delivery charge by hand rather than it being the
    /// sum of the products' own charges.
    /// </summary>
    /// <remarks>
    /// Kept on the order because otherwise the delivery line does not follow
    /// from the items on it and looks, to anyone auditing later, like a
    /// mistake.
    /// </remarks>
    public bool DeliveryOverridden { get; set; }

    /// <summary>
    /// A deposit required before work starts, for made-to-order items.
    /// </summary>
    /// <remarks>
    /// Null when nothing is required. Phase 3 fills this in; it is here now so
    /// that adding it is not a schema change on the busiest table in the shop.
    /// </remarks>
    public Money? RequiredAdvanceAmount { get; set; }

    // --- Status --------------------------------------------------------------

    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;

    public FulfilmentStatus FulfilmentStatus { get; set; } = FulfilmentStatus.Unfulfilled;

    /// <summary><c>cod</c>, <c>bkash</c> — matches a <c>PaymentMethodConfig.Code</c>.</summary>
    public string PaymentMethodCode { get; set; } = null!;

    public DateTimeOffset PlacedAt { get; set; }

    /// <summary>Staff-only. Never shown to the customer.</summary>
    public string? InternalNotes { get; set; }

    /// <summary>
    /// The cart this came from, kept as evidence.
    /// </summary>
    /// <remarks>
    /// "The customer says they ordered three chairs" is answerable against the
    /// basket that was checked out, which is why a checked-out cart is retired
    /// rather than deleted.
    /// </remarks>
    public long? CartId { get; set; }

    /// <summary>
    /// The client's key for this placement attempt.
    /// </summary>
    /// <remarks>
    /// <b>This is what stops a double-tap becoming two sofas.</b> A customer on
    /// a slow connection who presses "Place order" twice, or a browser that
    /// retries a timed-out POST, sends the same key — and the unique index on
    /// this column turns the second attempt into a lookup of the first order
    /// rather than a second one.
    /// </remarks>
    public string? IdempotencyKey { get; set; }

    public ICollection<OrderLine> Lines { get; set; } = [];

    public ICollection<OrderTimelineEntry> Timeline { get; set; } = [];
}

/// <summary>
/// One line of a placed order, with everything about it frozen.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every display field is copied, not joined.</b> The product name, the
/// variant name and the SKU are stored here even though
/// <see cref="ProductVariantId"/> could reach them. If the line rendered
/// through a live join, then renaming a product — or soft-deleting it — would
/// rewrite what an old invoice says was sold, and the invoice would stop
/// matching the paper the customer holds.
/// </para>
/// <para>
/// The variant id survives anyway, because "how many of this variant did we
/// sell in September" is a real question and a name match is not an answer.
/// </para>
/// </remarks>
public class OrderLine : BaseEntity
{
    public long OrderId { get; set; }

    public Order Order { get; set; } = null!;

    /// <summary>For reporting and reordering. Not what the line displays.</summary>
    public long ProductVariantId { get; set; }

    public ProductVariant? ProductVariant { get; set; }

    /// <summary>Kept alongside the variant so category reports need no join through it.</summary>
    public long ProductId { get; set; }

    public string ProductNameEn { get; set; } = null!;

    public string? ProductNameBn { get; set; }

    /// <summary>The URL the customer can still click from their order history.</summary>
    public string ProductSlug { get; set; } = null!;

    public string Sku { get; set; } = null!;

    /// <summary>"Segun · 6ft · Matte" — what distinguished this from its siblings.</summary>
    public string VariantName { get; set; } = null!;

    /// <summary>The thumbnail, so an old order still renders after the photo is replaced.</summary>
    public string? ImagePath { get; set; }

    public int Quantity { get; set; }

    /// <summary>What one cost, at placement.</summary>
    public Money UnitPrice { get; set; } = null!;

    /// <summary>Money taken off this line. Zero until the discount engine lands in Phase 3.</summary>
    public Money DiscountAmount { get; set; } = null!;

    /// <summary><see cref="UnitPrice"/> × <see cref="Quantity"/> − <see cref="DiscountAmount"/>.</summary>
    public Money LineTotal { get; set; } = null!;

    /// <summary>
    /// The delivery charge this line contributed, before any override.
    /// </summary>
    /// <remarks>
    /// Stored per line because delivery is priced per product: without it, "why
    /// is delivery 2,300৳ on this order" has no answer once the products'
    /// charges have been edited. The order's <c>DeliveryFee</c> remains what was
    /// actually charged — these need not add up to it when staff overrode the
    /// total, and <c>DeliveryOverridden</c> says so.
    /// </remarks>
    public Money DeliveryChargeApplied { get; set; } = null!;

    /// <summary>Working days quoted for a made-to-order item, at placement.</summary>
    public int? LeadTimeDays { get; set; }
}

/// <summary>
/// One entry in an order's history: who moved it, when, from where to where.
/// </summary>
/// <remarks>
/// <para>
/// Append-only. Rows are never updated or deleted, because the whole value of
/// this table is that it can be trusted — "who cancelled this order" is a
/// question that gets asked, and it needs an answer rather than a guess.
/// </para>
/// <para>
/// <see cref="ActorName"/> is copied rather than joined for the same reason as
/// the order lines: a staff member who leaves and is deactivated must not erase
/// their name from the record of what they did.
/// </para>
/// </remarks>
public class OrderTimelineEntry : BaseEntity
{
    public long OrderId { get; set; }

    public Order Order { get; set; } = null!;

    /// <summary>Null on the first entry — the order came from nowhere.</summary>
    public OrderStatus? FromStatus { get; set; }

    public OrderStatus ToStatus { get; set; }

    /// <summary>Null when the customer or a background job did it.</summary>
    public long? ActorUserId { get; set; }

    /// <summary>"Rakib (admin)", "Customer", "System" — readable a year later.</summary>
    public string ActorName { get; set; } = null!;

    /// <summary>Why. Shown to staff, and on cancellation to the customer too.</summary>
    public string? Note { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
