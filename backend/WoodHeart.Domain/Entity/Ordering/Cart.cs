using WoodHeart.Domain.Entity.Catalog;
using WoodHeart.Domain.Entity.Identity;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Domain.Entity.Ordering;

/// <summary>
/// A basket, belonging either to a signed-in customer or to a guest.
/// </summary>
/// <remarks>
/// <para>
/// <b>Guest carts are the main path, not an edge case.</b> Most people who buy
/// a bed here will do it without ever creating an account, so a cart is
/// identified by <see cref="CustomerId"/> <i>or</i> by
/// <see cref="AnonymousToken"/>, and exactly one of the two is set. Making the
/// guest case a first-class citizen of the model is what stops it becoming a
/// pile of null checks later.
/// </para>
/// <para>
/// <b>Totals are deliberately not stored here.</b> PLAN.md §6.3 lists
/// <c>Subtotal</c>, <c>DeliveryFee</c>, <c>VatAmount</c> and <c>GrandTotal</c>
/// as cart fields; they are computed by <c>CartPricer</c> on every read
/// instead. A stored total is a cached copy of something that changes whenever
/// a price is edited, a VAT rate is changed, or a delivery zone is picked — and
/// the failure mode is the worst kind: the cart page shows 5,000৳, checkout
/// charges 5,200৳, and nothing in the logs says why. Recomputing costs one
/// pass over a handful of lines.
/// </para>
/// <para>
/// An order is the opposite: its totals <i>are</i> stored, because they
/// must never change again.
/// </para>
/// </remarks>
public class Cart : BaseEntity
{
    /// <summary>Set when a signed-in customer owns this cart.</summary>
    public long? CustomerId { get; set; }

    public AppUser? Customer { get; set; }

    /// <summary>
    /// SHA-256 of the opaque token a guest's browser holds, never the token
    /// itself.
    /// </summary>
    /// <remarks>
    /// Hashed for the same reason a refresh token is: anyone who reads this
    /// column in a backup or a leaked query result would otherwise be able to
    /// present it and take over a stranger's basket — including their delivery
    /// address once checkout fills it in.
    /// </remarks>
    public string? AnonymousToken { get; set; }

    public CartStatus Status { get; set; } = CartStatus.Active;

    public string Currency { get; set; } = Money.Bdt;

    /// <summary>
    /// Where the customer wants it delivered, once they have said. Null until
    /// then, and the cart shows the delivery line as "calculated at checkout"
    /// rather than guessing at Dhaka.
    /// </summary>
    public DeliveryZone? DeliveryZone { get; set; }

    /// <summary>
    /// When an untouched cart stops counting as live.
    /// </summary>
    /// <remarks>
    /// Pushed forward on every change. It drives the abandoned-cart sweep, not
    /// a delete — the rows survive, because a cart abandoned four hours ago is
    /// the single best recovery email a shop can send.
    /// </remarks>
    public DateTimeOffset ExpiresAt { get; set; }

    public ICollection<CartLine> Lines { get; set; } = [];
}

/// <summary>
/// One variant, in a quantity, in a cart.
/// </summary>
/// <remarks>
/// <para>
/// The line points at a <see cref="ProductVariant"/> and nothing else. The
/// variant is what has a price and a stock count; the product is the marketing
/// page around it. Denormalising <c>ProductId</c> alongside would give two
/// sources of truth for the same fact and one of them would eventually be
/// wrong.
/// </para>
/// </remarks>
public class CartLine : BaseEntity
{
    public long CartId { get; set; }

    public Cart Cart { get; set; } = null!;

    public long ProductVariantId { get; set; }

    public ProductVariant ProductVariant { get; set; } = null!;

    public int Quantity { get; set; }

    /// <summary>
    /// The unit price when this line was added or last changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is not what the customer is charged.</b> Pricing always reads the
    /// variant's live price; this column exists so the cart can <i>say</i> that
    /// a price moved while the item sat in the basket. Someone who added a
    /// wardrobe at 42,000৳ three weeks ago and finds 45,000৳ at checkout should
    /// be told, not silently charged the difference.
    /// </para>
    /// <para>
    /// Snapshotting for real — freezing the price a customer pays — belongs on
    /// the order line, where it is an invariant. Doing it here would let anyone
    /// hold yesterday's price indefinitely by leaving a tab open.
    /// </para>
    /// </remarks>
    public Money UnitPriceAtAdd { get; set; } = null!;
}
