using Microsoft.Extensions.Logging;
using WoodHeart.Domain.Entity.Ordering;
using WoodHeart.Domain.Entity.Promotions;
using WoodHeart.Domain.Enums.Promotions;
using WoodHeart.Domain.Helpers;
using WoodHeart.Domain.Promotions;
using WoodHeart.Domain.ValueObjects;
using WoodHeart.Repository.Interfaces.Ordering;
using WoodHeart.Repository.Interfaces.Promotions;
using WoodHeart.Service.Interfaces.Promotions;

namespace WoodHeart.Service.Services.Promotions;

/// <inheritdoc cref="IPromotionService" />
public class PromotionService(
    IDiscountRepository discounts,
    IPromotionUsageRepository usages,
    IOrderRepository orders,
    IDateTimeProvider clock,
    ILogger<PromotionService> logger) : IPromotionService
{
    public async Task<DiscountOutcome> EvaluateAsync(
        PromotionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var currency = request.Lines.Count > 0 ? request.Lines[0].LineTotal.Currency : Money.Bdt;

        // An empty basket has nothing to discount, and asking the database
        // about it on every page load of an empty header is a query the shop
        // pays for a thousand times a day for nothing.
        if (request.Lines.Count == 0 && request.Codes.Count == 0)
        {
            return DiscountOutcome.None(currency);
        }

        var candidates = await discounts.GetCandidatesAsync(clock.UtcNow, request.Codes, cancellationToken);

        var usage = candidates.Count == 0
            ? new Dictionary<long, DiscountUsage>()
            : await usages.GetUsageAsync(
                [.. candidates.Select(candidate => candidate.Id)],
                request.CustomerId,
                request.ContactPhone,
                cancellationToken);

        var outcome = DiscountEngine.Evaluate(
            candidates,
            new DiscountContext(
                Lines: request.Lines,
                Now: clock.UtcNow,
                DeliveryFee: request.DeliveryFee,
                Zone: request.Zone,
                PaymentMethodCode: request.PaymentMethodCode,
                Codes: request.Codes,
                IsFirstOrder: await IsFirstOrderAsync(candidates, request, cancellationToken),
                Usage: usage));

        foreach (var rejected in outcome.Rejected)
        {
            PromotionLog.CouponRejected(logger, rejected.Code, rejected.Reason);
        }

        return outcome;
    }

    public async Task RecordUsageAsync(
        Order order,
        IReadOnlyList<AppliedDiscount> applied,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(applied);

        foreach (var discount in applied)
        {
            // A discount that gave nothing does not count as used. It happens
            // when staff have already set the delivery charge by hand and a
            // free-shipping coupon has nothing left to waive; burning one of
            // the customer's two allowed redemptions on that would be taking
            // something for nothing.
            if (!discount.Amount.IsPositive)
            {
                continue;
            }

            await usages.InsertAsync(
                new PromotionUsage
                {
                    DiscountId = discount.DiscountId,
                    OrderId = order.Id,
                    CustomerId = order.CustomerId,
                    ContactPhone = order.ContactPhone,
                    Code = discount.Code,
                    Amount = discount.Amount,
                    UsedAt = clock.UtcNow
                },
                cancellationToken);

            PromotionLog.DiscountApplied(
                logger, discount.Name, discount.Code, order.OrderNumber, discount.Amount.Amount);
        }
    }

    /// <summary>
    /// Whether this buyer has ordered before — asked only when it can matter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Skipped entirely unless some candidate is marked <c>FirstOrderOnly</c>,
    /// because this runs on every read of every basket and most shops have no
    /// such promotion at all.
    /// </para>
    /// <para>
    /// A guest browsing has given no phone number, so the answer is "yes, first
    /// order" and the basket shows the discount. The refusal, if there is one,
    /// comes at checkout where they have finally said who they are. That is the
    /// truthful order to discover it in: the alternative is hiding a
    /// new-customer discount from every new customer.
    /// </para>
    /// </remarks>
    private async Task<bool> IsFirstOrderAsync(
        IReadOnlyList<Discount> candidates,
        PromotionRequest request,
        CancellationToken cancellationToken)
    {
        if (!candidates.Any(candidate => candidate.FirstOrderOnly))
        {
            return true;
        }

        if (request.CustomerId is null && string.IsNullOrWhiteSpace(request.ContactPhone))
        {
            return true;
        }

        return !await orders.HasPlacedOrderAsync(
            request.CustomerId, request.ContactPhone, cancellationToken);
    }
}

/// <summary>
/// Structured logging for discounts.
/// </summary>
/// <remarks>
/// Event ids 2100–2102. <c>[LoggerMessage]</c> for the same reason as
/// everywhere else: the message template is compiled once rather than parsed on
/// every basket read.
/// </remarks>
internal static partial class PromotionLog
{
    [LoggerMessage(
        EventId = 2100,
        Level = LogLevel.Information,
        Message = "Discount {Name} ({Code}) gave {Amount} on order {OrderNumber}.")]
    public static partial void DiscountApplied(
        ILogger logger, string name, string? code, string orderNumber, decimal amount);

    /// <summary>
    /// A typed code that did not apply.
    /// </summary>
    /// <remarks>
    /// Debug rather than Warning: a customer trying an expired code is ordinary
    /// behaviour, not a fault, and at Information it would fill the log with
    /// other people's typing. It is here at all because "my code will not work"
    /// is a support call, and the answer is in this line.
    /// </remarks>
    [LoggerMessage(
        EventId = 2101,
        Level = LogLevel.Debug,
        Message = "Coupon {Code} was refused: {Reason}.")]
    public static partial void CouponRejected(ILogger logger, string code, string reason);

    [LoggerMessage(
        EventId = 2102,
        Level = LogLevel.Information,
        Message = "Discount {Name} was set to {Status} by {Actor}.")]
    public static partial void StatusChanged(ILogger logger, string name, DiscountStatus status, string actor);
}
