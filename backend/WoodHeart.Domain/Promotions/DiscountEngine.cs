using WoodHeart.Domain.Constants;
using WoodHeart.Domain.Entity.Promotions;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.Enums.Promotions;
using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Domain.Promotions;

/// <summary>
/// One basket line as the engine sees it.
/// </summary>
/// <remarks>
/// A flat record rather than the cart entity, for the same reason
/// <c>PricedLine</c> is: the engine must never touch an EF navigation, and it
/// has to be constructible from an order line as easily as from a cart line.
/// </remarks>
/// <param name="VariantId">Identifies the line. One line per variant, in a cart and on an order.</param>
/// <param name="ProductId">For a discount targeted at particular products.</param>
/// <param name="CategoryPath">
/// The product's category <c>MaterializedPath</c> — <c>/1/14/37/</c>. Carried
/// rather than the category id so that "20% off Bedroom" reaches the beds
/// filed under Bedroom → Beds without the engine needing the category tree.
/// </param>
/// <param name="Quantity">How many.</param>
/// <param name="LineTotal">Unit price times quantity, before any discount.</param>
public readonly record struct DiscountLine(
    long VariantId,
    long ProductId,
    string CategoryPath,
    int Quantity,
    Money LineTotal);

/// <summary>How often a discount has been used already.</summary>
/// <param name="Total">Across every customer.</param>
/// <param name="ByCustomer">By this customer — by account, or by phone number for a guest.</param>
public readonly record struct DiscountUsage(int Total, int ByCustomer);

/// <summary>
/// Everything outside the discount rows that decides what applies.
/// </summary>
/// <param name="Lines">The basket.</param>
/// <param name="Now">The clock, passed in rather than read — this function is pure.</param>
/// <param name="DeliveryFee">
/// What delivery would cost with no discount at all. It is the value of a free-shipping
/// discount, so the shop can report what its promotions cost it.
/// </param>
/// <param name="Zone">Where it is going, or null while the customer is still browsing.</param>
/// <param name="PaymentMethodCode">
/// Chosen at checkout, null on the basket page. A discount tied to bKash shows
/// on the basket and is refused if they then pay cash — which is the truthful
/// order to discover it in.
/// </param>
/// <param name="Codes">The coupon codes the customer has typed.</param>
/// <param name="IsFirstOrder">
/// Whether this buyer has ordered before. Unknown for a guest until they give
/// a phone number at checkout, and assumed true until then.
/// </param>
/// <param name="Usage">Redemption counts, by discount id. Missing means never used.</param>
public sealed record DiscountContext(
    IReadOnlyList<DiscountLine> Lines,
    DateTimeOffset Now,
    Money DeliveryFee,
    DeliveryZone? Zone = null,
    string? PaymentMethodCode = null,
    IReadOnlyCollection<string>? Codes = null,
    bool IsFirstOrder = true,
    IReadOnlyDictionary<long, DiscountUsage>? Usage = null);

/// <summary>
/// One discount that applied, and what it took off.
/// </summary>
/// <param name="PerVariant">
/// The allocation across the lines it applied to, keyed by variant id. This is
/// what an order line's <c>DiscountAmount</c> is filled from, and it sums
/// exactly to <paramref name="Amount"/> — see the remarks on
/// <see cref="DiscountEngine"/>.
/// </param>
public sealed record AppliedDiscount(
    long DiscountId,
    string Name,
    string? Code,
    DiscountType Type,
    Money Amount,
    IReadOnlyDictionary<long, Money> PerVariant);

/// <summary>A code the customer typed that did not apply, and why.</summary>
/// <param name="Reason">A <see cref="PromotionErrors"/> code, so the client can word it.</param>
public sealed record RejectedCoupon(string Code, string Reason);

/// <summary>What the engine decided.</summary>
/// <param name="GoodsDiscount">
/// The total to take off the goods. Never more than the basket — a discount
/// larger than what is being bought makes it free, it does not make the shop
/// owe money.
/// </param>
/// <param name="FreeShipping">Whether a discount waives the delivery charge.</param>
public sealed record DiscountOutcome(
    IReadOnlyList<AppliedDiscount> Applied,
    Money GoodsDiscount,
    bool FreeShipping,
    IReadOnlyList<RejectedCoupon> Rejected)
{
    public static DiscountOutcome None(string currency = Money.Bdt) =>
        new([], Money.Zero(currency), false, []);
}

/// <summary>
/// Decides which discounts apply to a basket and for how much.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pure: no database, no clock, no randomness.</b> The clock arrives in
/// <see cref="DiscountContext.Now"/> and the redemption counts arrive in
/// <see cref="DiscountContext.Usage"/>, because the reason this is a function
/// rather than a service is that the <i>same</i> function runs at basket
/// preview and at order placement. Two implementations of "what is this worth"
/// always drift, and when they drift the customer is shown one price and
/// charged another — PLAN.md §12 lists this, the checkout total and stock
/// reservation as the three places money is lost silently.
/// </para>
/// <para>
/// <b>The conditions are measured on the same items the discount comes off.</b>
/// An untargeted discount measures its minimum spend and minimum quantity
/// against the whole basket; one targeted at sofas measures them against the
/// sofas. One rule, which is why "spend 20,000৳ on sofas, get 10% off" needs no
/// special case — and why "spend 20,000৳ on anything, get 10% off sofas" is
/// deliberately not expressible. That second rule can be added the day a shop
/// asks for it, as a separate minimum; guessing at it now would mean guessing
/// which of the two every existing discount meant.
/// </para>
/// <para>
/// <b>Every discount is computed against the undiscounted basket, never
/// against what is left after the previous one.</b> Compounding is the more
/// sophisticated answer and the wrong one: "20% off and 500৳ off" has to come
/// to a figure the shop can predict from the two rules, and an order that
/// depends on which discount was evaluated first is one nobody can explain to
/// a customer. The total is then capped at the basket.
/// </para>
/// <para>
/// <b>Resolution is by priority, then by value to the customer.</b> A
/// non-stackable discount takes the basket alone: it is considered first in
/// that order and, if taken, nothing else applies. Where nothing is
/// non-stackable, everything that qualifies applies. Ties break on the
/// discount id, so the same basket always produces the same bill.
/// </para>
/// </remarks>
public static class DiscountEngine
{
    public static DiscountOutcome Evaluate(
        IReadOnlyList<Discount> candidates, DiscountContext context)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(context);

        var currency = context.Lines.Count > 0 ? context.Lines[0].LineTotal.Currency : Money.Bdt;
        var zero = Money.Zero(currency);
        var subtotal = context.Lines.Aggregate(zero, (running, line) => running + line.LineTotal);

        var codes = Normalise(context.Codes);
        var rejected = new List<RejectedCoupon>();

        // Everything that could apply, with what it would be worth on its own.
        var qualified = new List<Candidate>();

        foreach (var discount in candidates)
        {
            // A coupon nobody typed is not offered and not refused. Its own
            // customers have it; this basket simply has no claim on it.
            if (discount.IsCoupon && !codes.Contains(discount.Code!))
            {
                continue;
            }

            var eligible = EligibleLines(discount, context.Lines);
            var reason = Disqualify(discount, context, eligible, currency);

            if (reason is not null)
            {
                if (discount.IsCoupon)
                {
                    rejected.Add(new RejectedCoupon(discount.Code!, reason));
                }

                continue;
            }

            qualified.Add(new Candidate(discount, eligible, Value(discount, context, eligible, currency)));
        }

        // A code with no discount behind it at all. Checked against the
        // candidates rather than the qualified list, so an expired code says
        // "expired" rather than "no such code".
        foreach (var code in codes.Where(code =>
                     !candidates.Any(d => string.Equals(d.Code, code, StringComparison.Ordinal))))
        {
            rejected.Add(new RejectedCoupon(code, PromotionErrors.CouponNotFound));
        }

        var applied = Select(qualified, subtotal, zero, rejected);

        var goodsDiscount = applied
            .Where(a => a.Type != DiscountType.FreeShipping)
            .Aggregate(zero, (running, a) => running + a.Amount);

        return new DiscountOutcome(
            applied,
            goodsDiscount,
            applied.Any(a => a.Type == DiscountType.FreeShipping),
            rejected);
    }

    // -------------------------------------------------------------------------
    // Selection
    // -------------------------------------------------------------------------

    /// <summary>
    /// Walks the qualified discounts in resolution order and takes what fits.
    /// </summary>
    private static List<AppliedDiscount> Select(
        List<Candidate> qualified, Money subtotal, Money zero, List<RejectedCoupon> rejected)
    {
        var applied = new List<AppliedDiscount>();
        var takenGoods = zero;
        var exclusiveTaken = false;

        foreach (var candidate in qualified
                     .OrderByDescending(c => c.Discount.Priority)
                     .ThenByDescending(c => c.Value.Amount)
                     .ThenBy(c => c.Discount.Id))
        {
            var blocked =
                exclusiveTaken || (!candidate.Discount.Stackable && applied.Count > 0);

            if (blocked)
            {
                if (candidate.Discount.IsCoupon)
                {
                    rejected.Add(new RejectedCoupon(
                        candidate.Discount.Code!, PromotionErrors.CouponNotCombinable));
                }

                continue;
            }

            var amount = candidate.Value;

            if (candidate.Discount.Type != DiscountType.FreeShipping)
            {
                // Capped at what is left of the basket, so the recorded amounts
                // always sum to the discount actually given. A second discount
                // that would take a free basket below zero is recorded as
                // taking nothing rather than as taking money the shop never had.
                amount = amount.CapAt((subtotal - takenGoods).OrZeroIfNegative());
                takenGoods += amount;
            }

            applied.Add(new AppliedDiscount(
                candidate.Discount.Id,
                candidate.Discount.Name,
                candidate.Discount.Code,
                candidate.Discount.Type,
                amount,
                Allocate(candidate, amount, zero)));

            if (!candidate.Discount.Stackable)
            {
                exclusiveTaken = true;
            }
        }

        return applied;
    }

    /// <summary>
    /// Spreads a discount across the lines it applied to, in proportion to what
    /// they cost.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order line stores what came off it, so a refund of one item out of
    /// four refunds what that item was actually charged. Rounding the shares
    /// independently would lose or gain a poisha, and an invoice whose lines do
    /// not add up to its own total is the kind of thing an auditor stops on —
    /// so the largest line absorbs the remainder.
    /// </para>
    /// <para>
    /// Free shipping allocates nothing: it comes off the delivery line, not off
    /// any product.
    /// </para>
    /// </remarks>
    private static Dictionary<long, Money> Allocate(
        Candidate candidate, Money amount, Money zero)
    {
        if (candidate.Discount.Type == DiscountType.FreeShipping
            || candidate.Eligible.Count == 0
            || !amount.IsPositive)
        {
            return new Dictionary<long, Money>();
        }

        var basis = candidate.Eligible.Aggregate(zero, (running, line) => running + line.LineTotal);

        if (!basis.IsPositive)
        {
            return new Dictionary<long, Money>();
        }

        var allocation = new Dictionary<long, Money>();
        var running = zero;

        foreach (var line in candidate.Eligible)
        {
            var share = amount.Multiply(line.LineTotal.Amount / basis.Amount);

            allocation[line.VariantId] = share;
            running += share;
        }

        var remainder = amount - running;

        if (!remainder.IsZero)
        {
            var largest = candidate.Eligible
                .OrderByDescending(line => line.LineTotal.Amount)
                .ThenBy(line => line.VariantId)
                .First();

            allocation[largest.VariantId] += remainder;
        }

        return allocation;
    }

    // -------------------------------------------------------------------------
    // Eligibility
    // -------------------------------------------------------------------------

    /// <summary>
    /// The lines a discount applies to: all of them, or the ones its targets
    /// name.
    /// </summary>
    /// <remarks>
    /// A category target matches the category and everything beneath it, which
    /// is why the line carries the materialized path. It relies on the target's
    /// own category being loaded — <c>IDiscountRepository</c> includes it on
    /// every read for exactly this reason.
    /// </remarks>
    private static IReadOnlyList<DiscountLine> EligibleLines(
        Discount discount, IReadOnlyList<DiscountLine> lines)
    {
        if (discount.Targets.Count == 0)
        {
            return lines;
        }

        return [.. lines.Where(line => discount.Targets.Any(target => Matches(target, line)))];
    }

    private static bool Matches(DiscountTarget target, DiscountLine line)
    {
        if (target.ProductId is { } productId)
        {
            return line.ProductId == productId;
        }

        return target.Category is { } category
               && category.MaterializedPath.Length > 0
               && line.CategoryPath.StartsWith(category.MaterializedPath, StringComparison.Ordinal);
    }

    /// <summary>Why this discount does not apply, or null when it does.</summary>
    private static string? Disqualify(
        Discount discount,
        DiscountContext context,
        IReadOnlyList<DiscountLine> eligible,
        string currency)
    {
        if (discount.Status != DiscountStatus.Active)
        {
            return PromotionErrors.CouponInactive;
        }

        if (discount.StartsAt is { } starts && context.Now < starts)
        {
            return PromotionErrors.CouponNotStarted;
        }

        if (discount.EndsAt is { } ends && context.Now >= ends)
        {
            return PromotionErrors.CouponExpired;
        }

        if (discount.DeliveryZones.Count > 0
            && context.Zone is { } zone
            && !discount.DeliveryZones.Contains(zone))
        {
            return PromotionErrors.CouponZone;
        }

        if (discount.PaymentMethods.Count > 0
            && context.PaymentMethodCode is { Length: > 0 } code
            && !discount.PaymentMethods.Contains(code, StringComparer.OrdinalIgnoreCase))
        {
            return PromotionErrors.CouponPaymentMethod;
        }

        if (discount.FirstOrderOnly && !context.IsFirstOrder)
        {
            return PromotionErrors.CouponFirstOrderOnly;
        }

        var usage = context.Usage is not null && context.Usage.TryGetValue(discount.Id, out var used)
            ? used
            : default;

        if (discount.UsageLimitTotal is { } total && usage.Total >= total)
        {
            return PromotionErrors.CouponLimitReached;
        }

        if (discount.UsageLimitPerCustomer is { } perCustomer && usage.ByCustomer >= perCustomer)
        {
            return PromotionErrors.CouponCustomerLimitReached;
        }

        // Free shipping applies to the order, not to the goods, so it survives
        // an empty eligible set only in the sense that nothing targets it —
        // a targeted free-shipping discount still needs its items present.
        if (eligible.Count == 0)
        {
            return PromotionErrors.CouponNoEligibleItems;
        }

        var basis = eligible.Aggregate(Money.Zero(currency), (running, line) => running + line.LineTotal);

        if (discount.MinSubtotal is { } minimum && basis < minimum)
        {
            return PromotionErrors.CouponMinSubtotal;
        }

        if (discount.MinQuantity is { } minimumQuantity
            && eligible.Sum(line => line.Quantity) < minimumQuantity)
        {
            return PromotionErrors.CouponMinQuantity;
        }

        // Nothing to take off. Reached when every eligible line is priced at
        // zero, which is rare and still better answered than silently applied.
        return discount.Type != DiscountType.FreeShipping && !basis.IsPositive
            ? PromotionErrors.CouponNoEligibleItems
            : null;
    }

    /// <summary>What one discount is worth on its own, before any capping.</summary>
    private static Money Value(
        Discount discount,
        DiscountContext context,
        IReadOnlyList<DiscountLine> eligible,
        string currency)
    {
        var zero = Money.Zero(currency);

        if (discount.Type == DiscountType.FreeShipping)
        {
            return context.DeliveryFee.OrZeroIfNegative();
        }

        var basis = eligible.Aggregate(zero, (running, line) => running + line.LineTotal);

        var raw = discount.Type switch
        {
            DiscountType.Percentage => basis.Percentage(Math.Clamp(discount.Value, 0m, 100m)),
            DiscountType.FixedAmount => Money.From(Math.Max(discount.Value, 0m), currency),
            _ => zero
        };

        if (discount.MaxDiscountAmount is { } ceiling)
        {
            raw = raw.CapAt(ceiling);
        }

        // Never more than the items it applies to. A 5,000৳ coupon on a 3,000৳
        // chair makes the chair free; it does not start paying for the sofa
        // next to it, which is what an uncapped fixed amount would do once the
        // allocation spread it.
        return raw.CapAt(basis);
    }

    private static HashSet<string> Normalise(IReadOnlyCollection<string>? codes) =>
        codes is null
            ? []
            : [.. codes
                .Select(Discount.NormaliseCode)
                .Where(code => code is not null)
                .Select(code => code!)];

    private readonly record struct Candidate(
        Discount Discount, IReadOnlyList<DiscountLine> Eligible, Money Value);
}
