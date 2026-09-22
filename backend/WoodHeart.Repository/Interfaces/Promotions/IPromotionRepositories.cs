using WoodHeart.Domain.Entity.Promotions;
using WoodHeart.Domain.Enums.Promotions;
using WoodHeart.Domain.Promotions;

namespace WoodHeart.Repository.Interfaces.Promotions;

/// <summary>
/// Finds discounts. Stages changes; does not commit them.
/// </summary>
/// <remarks>
/// <b>Every read here includes <c>Targets</c> and each target's category.</b>
/// <c>DiscountEngine</c> matches a category target against a line's
/// materialized path, which it reads off the target's own category — a
/// discount loaded without it would silently apply to nothing, which is the
/// worst shape a bug about money can take. The includes are here rather than
/// at the call sites so no caller can forget.
/// </remarks>
public interface IDiscountRepository : IRepository<Discount>
{
    /// <summary>
    /// Everything that could apply to a basket right now: the live automatic
    /// promotions, plus any discount whose code the customer has typed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two halves are filtered differently on purpose. An automatic
    /// promotion that has expired is of no interest to anyone, so it is left in
    /// the database. A <i>coupon</i> that has expired is very much of interest:
    /// somebody has just typed it, and "that code ran until the 14th" is a
    /// better answer than "no such code". So a typed code is loaded whatever
    /// state it is in, and the engine says why it did not apply.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<Discount>> GetCandidatesAsync(
        DateTimeOffset now,
        IReadOnlyCollection<string> codes,
        CancellationToken cancellationToken = default);

    /// <summary>One discount by its code, in any state. Codes are upper-cased.</summary>
    Task<Discount?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>One discount with its targets, for the admin screens.</summary>
    Task<Discount?> GetWithTargetsAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>The admin list: newest first, filtered by status and by a text match.</summary>
    Task<PagedList<Discount>> SearchAsync(
        string? term,
        DiscountStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Whether another discount already holds this code.</summary>
    Task<bool> CodeExistsAsync(
        string code, long? excludeId = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// The record of what discounts have actually been given.
/// </summary>
public interface IPromotionUsageRepository : IRepository<PromotionUsage>
{
    /// <summary>
    /// How often each of these discounts has been redeemed, in total and by
    /// this one customer.
    /// </summary>
    /// <remarks>
    /// One grouped query rather than one per discount: this runs on every
    /// basket read, and a query per candidate would make a shop with a dozen
    /// promotions pay for them on its busiest page.
    /// </remarks>
    Task<IReadOnlyDictionary<long, DiscountUsage>> GetUsageAsync(
        IReadOnlyCollection<long> discountIds,
        long? customerId,
        string? contactPhone,
        CancellationToken cancellationToken = default);

    /// <summary>The usage report for one discount, newest first.</summary>
    Task<PagedList<PromotionUsage>> SearchAsync(
        long discountId, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>What one discount has cost the shop, and over how many orders.</summary>
    Task<(int Orders, decimal Total)> TotalsAsync(
        long discountId, CancellationToken cancellationToken = default);
}
