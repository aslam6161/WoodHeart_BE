using WoodHeart.Domain.Entity.Promotions;
using WoodHeart.Domain.Enums.Promotions;
using WoodHeart.Service.DTOs.Promotions;

namespace WoodHeart.Service.Mapping.Promotions;

/// <summary>
/// Discount entities to the shapes the admin screens render.
/// </summary>
/// <remarks>
/// Hand-written, like the rest. The one decision worth seeing here is
/// <see cref="IsLive"/>: status and window are separate facts and the list has
/// to combine them, because "Active" on a campaign that starts next month is
/// technically true and practically misleading.
/// </remarks>
public static class PromotionMapper
{
    public static DiscountListItemDto ToListItem(Discount discount, DateTimeOffset now, int timesUsed) =>
        new()
        {
            Id = discount.Id,
            Name = discount.Name,
            Code = discount.Code,
            Type = discount.Type,
            Value = discount.Value,
            Status = discount.Status,
            StartsAt = discount.StartsAt,
            EndsAt = discount.EndsAt,
            UsageLimitTotal = discount.UsageLimitTotal,
            TimesUsed = timesUsed,
            IsLive = IsLive(discount, now)
        };

    public static DiscountDto ToDto(
        Discount discount, DateTimeOffset now, int timesUsed, decimal totalGiven) =>
        new()
        {
            Id = discount.Id,
            Name = discount.Name,
            Code = discount.Code,
            Type = discount.Type,
            Value = discount.Value,
            Status = discount.Status,
            StartsAt = discount.StartsAt,
            EndsAt = discount.EndsAt,
            UsageLimitTotal = discount.UsageLimitTotal,
            TimesUsed = timesUsed,
            IsLive = IsLive(discount, now),

            MaxDiscountAmount = discount.MaxDiscountAmount?.Amount,
            MinSubtotal = discount.MinSubtotal?.Amount,
            MinQuantity = discount.MinQuantity,
            FirstOrderOnly = discount.FirstOrderOnly,
            DeliveryZones = [.. discount.DeliveryZones],
            PaymentMethods = [.. discount.PaymentMethods],
            UsageLimitPerCustomer = discount.UsageLimitPerCustomer,
            Stackable = discount.Stackable,
            Priority = discount.Priority,
            Targets = [.. discount.Targets.Select(ToTargetDto)],
            TotalGiven = totalGiven
        };

    public static PromotionUsageDto ToUsageDto(PromotionUsage usage) =>
        new()
        {
            OrderId = usage.OrderId,
            OrderNumber = usage.Order?.OrderNumber ?? string.Empty,
            Code = usage.Code,
            ContactPhone = usage.ContactPhone,
            IsMember = usage.CustomerId is not null,
            Amount = usage.Amount.Amount,
            UsedAt = usage.UsedAt
        };

    /// <summary>
    /// Whether it would apply to a qualifying basket right now.
    /// </summary>
    /// <remarks>
    /// Deliberately does not consider usage limits or basket conditions — those
    /// are about one customer's basket, and this is about whether the campaign
    /// is running at all.
    /// </remarks>
    private static bool IsLive(Discount discount, DateTimeOffset now) =>
        discount.Status == DiscountStatus.Active
        && (discount.StartsAt is not { } starts || starts <= now)
        && (discount.EndsAt is not { } ends || ends > now);

    private static DiscountTargetDto ToTargetDto(DiscountTarget target) =>
        new()
        {
            CategoryId = target.CategoryId,
            ProductId = target.ProductId,

            // Null when the target was loaded without its navigation. The edit
            // screen falls back to the id rather than showing a blank chip.
            Name = target.Category?.Name.En ?? target.Product?.Name.En
        };
}
