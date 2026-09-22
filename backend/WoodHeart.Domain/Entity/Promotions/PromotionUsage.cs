using WoodHeart.Domain.Entity.Identity;
using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Domain.Entity.Promotions;

/// <summary>
/// One order's use of one discount: who, how much, and when.
/// </summary>
/// <remarks>
/// <para>
/// <b>This table is what makes a usage limit real.</b> A counter on the
/// discount row would be cheaper to read and impossible to trust: two
/// checkouts a millisecond apart both read 99, both write 100, and a coupon
/// capped at 100 uses is redeemed 101 times with nothing in the database to
/// show how. A row per use is the evidence, and counting them is a cheap
/// indexed query.
/// </para>
/// <para>
/// <b><see cref="ContactPhone"/> is stored beside <see cref="CustomerId"/>
/// because most buyers here have no account.</b> "One per customer" has to
/// mean something for a guest, and the normalised phone number is the only
/// identity a guest order has. It is the same key that hands a guest their
/// history when they register later.
/// </para>
/// <para>
/// Written in the same transaction as the order. A use recorded after the
/// commit is a limit that a crash can silently raise.
/// </para>
/// </remarks>
public class PromotionUsage : BaseEntity
{
    public long DiscountId { get; set; }

    public Discount Discount { get; set; } = null!;

    public long OrderId { get; set; }

    public Order Order { get; set; } = null!;

    /// <summary>Set when the buyer had an account. Null for a guest order.</summary>
    public long? CustomerId { get; set; }

    public AppUser? Customer { get; set; }

    /// <summary>E.164, as the order stores it. The guest's identity for a per-customer limit.</summary>
    public string ContactPhone { get; set; } = null!;

    /// <summary>
    /// The code that was typed, snapshotted.
    /// </summary>
    /// <remarks>
    /// Null for an automatic promotion. Copied rather than joined so that
    /// renaming a coupon — reusing <c>EID25</c> next year — does not rewrite
    /// what last year's customer actually typed.
    /// </remarks>
    public string? Code { get; set; }

    /// <summary>What it took off this order.</summary>
    public Money Amount { get; set; } = null!;

    public DateTimeOffset UsedAt { get; set; }
}
