using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.Enums.Promotions;
using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Domain.Entity.Promotions;

/// <summary>
/// A rule that takes money off a basket: an automatic promotion, or a coupon
/// somebody types.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="Code"/> is the only thing that separates the two kinds.</b> A
/// discount with a code has to be typed in to apply; one without applies to
/// every basket that qualifies. Everything else — the window, the conditions,
/// the limits — is identical, which is why "run the September sale, and also
/// give the newsletter list a code" is one table and one engine rather than
/// two of each.
/// </para>
/// <para>
/// <b>PLAN.md §6.5 has a <c>Scope</c> field and this does not.</b> Scope
/// (Cart | LineItem) and the product/category conditions say overlapping
/// things, and they can contradict each other — a cart-scoped discount
/// restricted to one category has two readable meanings and no correct one.
/// <see cref="Targets"/> alone carries it instead: a discount with no targets
/// applies to the whole basket, one with targets applies to the lines that
/// match. The engine allocates the result across those lines either way, so
/// nothing downstream needs to know which kind it was.
/// </para>
/// <para>
/// Nothing here is evaluated. <c>DiscountEngine</c> decides what applies and
/// for how much; this is the configuration it reads. That separation is the
/// point of the engine being pure — see its remarks.
/// </para>
/// </remarks>
public class Discount : BaseEntity
{
    /// <summary>What the shop calls it. Shown to the customer on the basket.</summary>
    /// <remarks>
    /// Shown, so it is written for a customer: "September sale", not
    /// "sept-2026-20pc-v2". The basket says what came off and why, and a
    /// discount whose name is an internal slug makes the bill look like a bug.
    /// </remarks>
    public string Name { get; set; } = null!;

    /// <summary>
    /// The code a customer types, or null for an automatic promotion.
    /// </summary>
    /// <remarks>
    /// Stored upper-cased and compared exactly, because the alternative —
    /// case-insensitive comparison in the query — cannot use a plain unique
    /// index. Callers normalise on the way in; see <see cref="NormaliseCode"/>.
    /// </remarks>
    public string? Code { get; set; }

    public DiscountType Type { get; set; }

    /// <summary>
    /// The percentage, or the amount in taka, depending on <see cref="Type"/>.
    /// Ignored for <see cref="DiscountType.FreeShipping"/>.
    /// </summary>
    public decimal Value { get; set; }

    /// <summary>
    /// The most this may ever take off, whatever the arithmetic says.
    /// </summary>
    /// <remarks>
    /// <b>This is what stops "20% off" costing 14,000৳ on a wardrobe.</b> A
    /// percentage discount written for a 5,000৳ chair is a different promotion
    /// entirely when somebody applies it to the most expensive thing in the
    /// catalogue, and the shop finds out at the end of the month.
    /// </remarks>
    public Money? MaxDiscountAmount { get; set; }

    // --- Conditions ----------------------------------------------------------

    /// <summary>Goods total the basket must reach. Null means no minimum.</summary>
    public Money? MinSubtotal { get; set; }

    /// <summary>Items the basket must contain, counted across the lines it applies to.</summary>
    public int? MinQuantity { get; set; }

    /// <summary>
    /// Only for somebody who has not ordered before.
    /// </summary>
    /// <remarks>
    /// "Before" is by account for a signed-in customer and by phone number for
    /// a guest. A guest browsing has given neither, so the basket shows the
    /// discount and checkout is where it can actually be refused — see
    /// <c>DiscountEngine</c>.
    /// </remarks>
    public bool FirstOrderOnly { get; set; }

    /// <summary>Zones it applies in. Empty means anywhere the shop delivers.</summary>
    public List<DeliveryZone> DeliveryZones { get; set; } = [];

    /// <summary>Payment method codes it applies to. Empty means any.</summary>
    /// <remarks>
    /// The field behind "5% off when you pay with bKash": a gateway's fee is
    /// lower than the cost of handling cash, and the shop may want to pay a
    /// customer to use it.
    /// </remarks>
    public List<string> PaymentMethods { get; set; } = [];

    /// <summary>
    /// The products and categories this applies to. Empty means the whole
    /// basket.
    /// </summary>
    public ICollection<DiscountTarget> Targets { get; set; } = [];

    // --- Window and limits ----------------------------------------------------

    /// <summary>When it starts. Null means it is live as soon as it is Active.</summary>
    public DateTimeOffset? StartsAt { get; set; }

    /// <summary>When it stops. Null means it runs until somebody stops it.</summary>
    public DateTimeOffset? EndsAt { get; set; }

    /// <summary>How many orders may use it in total. Null means unlimited.</summary>
    public int? UsageLimitTotal { get; set; }

    /// <summary>How many times one customer may use it. Null means unlimited.</summary>
    public int? UsageLimitPerCustomer { get; set; }

    /// <summary>
    /// Whether it may be combined with others.
    /// </summary>
    /// <remarks>
    /// Defaults to false, which is the answer that cannot lose money by
    /// accident. Two stackable 20% discounts on one basket is 36% off, and
    /// nobody who wrote either of them meant that.
    /// </remarks>
    public bool Stackable { get; set; }

    /// <summary>Higher wins when two discounts cannot both apply.</summary>
    public int Priority { get; set; }

    public DiscountStatus Status { get; set; } = DiscountStatus.Draft;

    /// <summary>True when this is a coupon rather than an automatic promotion.</summary>
    public bool IsCoupon => !string.IsNullOrWhiteSpace(Code);

    /// <summary>
    /// The stored form of a code: trimmed and upper-cased.
    /// </summary>
    /// <remarks>
    /// In one place because the admin writing a code, the customer typing one
    /// and the unique index all have to agree on what "the same code" means.
    /// Invariant culture, so a Turkish locale cannot turn <c>i</c> into
    /// <c>İ</c> and quietly create a second, unreachable coupon.
    /// </remarks>
    public static string? NormaliseCode(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();
}

/// <summary>
/// One thing a discount is restricted to: a product, or a whole category.
/// </summary>
/// <remarks>
/// <para>
/// One row per restriction rather than two nullable columns on the discount,
/// because "20% off these four sofas" is as ordinary a request as "20% off
/// sofas" and a single pair of columns cannot hold it.
/// </para>
/// <para>
/// Exactly one of the two ids is set. The database says so as well as the code
/// does — see the check constraint in the configuration — because a row with
/// neither would silently match nothing and a row with both would match twice.
/// </para>
/// </remarks>
public class DiscountTarget : BaseEntity
{
    public long DiscountId { get; set; }

    public Discount Discount { get; set; } = null!;

    /// <summary>Set when this restriction is a whole category.</summary>
    public long? CategoryId { get; set; }

    public Category? Category { get; set; }

    /// <summary>Set when this restriction is one product.</summary>
    public long? ProductId { get; set; }

    public Product? Product { get; set; }
}
