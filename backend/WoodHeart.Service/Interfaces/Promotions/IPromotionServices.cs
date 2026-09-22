using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.Enums.Promotions;
using WoodHeart.Domain.Promotions;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository;
using WoodHeart.Service.DTOs.Ordering;
using WoodHeart.Service.DTOs.Promotions;

namespace WoodHeart.Service.Interfaces.Promotions;

/// <summary>
/// What the engine needs that only the database knows.
/// </summary>
/// <param name="Lines">The basket, already filtered to what can actually be bought.</param>
/// <param name="DeliveryFee">Delivery priced with no discount — the value of free shipping.</param>
/// <param name="Zone">Where it is going, or null while the customer is still browsing.</param>
/// <param name="PaymentMethodCode">Chosen at checkout, null on the basket page.</param>
/// <param name="Codes">The codes typed onto the basket.</param>
/// <param name="CustomerId">Set when the buyer is signed in.</param>
/// <param name="ContactPhone">
/// Known at checkout, and for a guest it is the only identity there is. Both a
/// per-customer limit and "new customers only" are counted on it.
/// </param>
public sealed record PromotionRequest(
    IReadOnlyList<DiscountLine> Lines,
    Money DeliveryFee,
    DeliveryZone? Zone,
    string? PaymentMethodCode,
    IReadOnlyCollection<string> Codes,
    long? CustomerId,
    string? ContactPhone);

/// <summary>
/// The database half of the discount engine: fetch the candidates, count the
/// redemptions, then hand it all to the pure function.
/// </summary>
/// <remarks>
/// The deciding is <c>DiscountEngine</c>'s and stays there. This service exists
/// so that the basket page and order placement fetch the same inputs as well as
/// running the same function — the two halves of "the price you were shown is
/// the price you are charged".
/// </remarks>
public interface IPromotionService
{
    Task<DiscountOutcome> EvaluateAsync(
        PromotionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records what each discount gave this order, in the caller's unit of work.
    /// </summary>
    /// <remarks>
    /// Staged, not committed. A redemption that committed on its own would
    /// survive a placement that rolled back, and the coupon capped at 100 uses
    /// would be a coupon capped at 100 attempts.
    /// </remarks>
    Task RecordUsageAsync(
        Order order,
        IReadOnlyList<AppliedDiscount> applied,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The admin's side: writing discounts, and reading what they have cost.
/// </summary>
public interface IDiscountAdminService
{
    Task<GeneralResponse<PagedResult<DiscountListItemDto>>> SearchAsync(
        DiscountQueryDto query, CancellationToken cancellationToken = default);

    Task<GeneralResponse<DiscountDto>> GetAsync(long id, CancellationToken cancellationToken = default);

    Task<GeneralResponse<DiscountDto>> CreateAsync(
        SaveDiscountDto dto, CancellationToken cancellationToken = default);

    Task<GeneralResponse<DiscountDto>> UpdateAsync(
        long id, SaveDiscountDto dto, CancellationToken cancellationToken = default);

    /// <summary>Draft, Active, Paused or Archived — the switch, without an edit.</summary>
    Task<GeneralResponse<DiscountDto>> SetStatusAsync(
        long id, DiscountStatus status, CancellationToken cancellationToken = default);

    /// <summary>Who used it, on what order, and for how much.</summary>
    Task<GeneralResponse<PagedResult<PromotionUsageDto>>> GetUsageAsync(
        long id, int page, int pageSize, CancellationToken cancellationToken = default);
}
