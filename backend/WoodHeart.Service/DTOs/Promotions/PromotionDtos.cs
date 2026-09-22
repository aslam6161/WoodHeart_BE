using System.ComponentModel.DataAnnotations;
using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.Enums.Promotions;

namespace WoodHeart.Service.DTOs.Promotions;

/// <summary>One row of the admin's discount list.</summary>
public class DiscountListItemDto
{
    public long Id { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>Null for an automatic promotion.</summary>
    public string? Code { get; init; }

    public DiscountType Type { get; init; }

    public decimal Value { get; init; }

    public DiscountStatus Status { get; init; }

    public DateTimeOffset? StartsAt { get; init; }

    public DateTimeOffset? EndsAt { get; init; }

    public int? UsageLimitTotal { get; init; }

    /// <summary>How many orders have used it.</summary>
    public int TimesUsed { get; init; }

    /// <summary>
    /// True when it would apply to a qualifying basket right now.
    /// </summary>
    /// <remarks>
    /// Computed rather than stored: Active and inside its window are two
    /// different facts, and a list that showed only the status would leave the
    /// shop guessing which of its "Active" campaigns are actually running
    /// today.
    /// </remarks>
    public bool IsLive { get; init; }
}

/// <summary>One discount in full, for the edit screen.</summary>
public class DiscountDto : DiscountListItemDto
{
    public decimal? MaxDiscountAmount { get; init; }

    public decimal? MinSubtotal { get; init; }

    public int? MinQuantity { get; init; }

    public bool FirstOrderOnly { get; init; }

    /// <summary>Empty means anywhere the shop delivers.</summary>
    public IReadOnlyList<DeliveryZone> DeliveryZones { get; init; } = [];

    /// <summary>Empty means any payment method.</summary>
    public IReadOnlyList<string> PaymentMethods { get; init; } = [];

    public int? UsageLimitPerCustomer { get; init; }

    public bool Stackable { get; init; }

    public int Priority { get; init; }

    /// <summary>What it applies to. Empty means the whole basket.</summary>
    public IReadOnlyList<DiscountTargetDto> Targets { get; init; } = [];

    /// <summary>What it has cost the shop so far.</summary>
    public decimal TotalGiven { get; init; }
}

/// <summary>A product or a category a discount is restricted to.</summary>
/// <remarks>
/// Exactly one id is set. The name comes back on a read so the edit screen can
/// render a chip without a second round trip, and is ignored on a write.
/// </remarks>
public class DiscountTargetDto
{
    public long? CategoryId { get; init; }

    public long? ProductId { get; init; }

    /// <summary>Read-only. What to show on the chip.</summary>
    public string? Name { get; init; }
}

/// <summary>Creating or replacing a discount. All-or-nothing, like the settings form.</summary>
public class SaveDiscountDto
{
    [Required]
    [StringLength(160, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Leave blank for an automatic promotion. Stored upper-cased.
    /// </summary>
    [StringLength(40, MinimumLength = 3)]
    [RegularExpression("^[A-Za-z0-9._-]+$",
        ErrorMessage = "A code may contain letters, digits, dots, dashes and underscores.")]
    public string? Code { get; init; }

    public DiscountType Type { get; init; }

    /// <summary>A percentage for <c>Percentage</c>, taka for <c>FixedAmount</c>, ignored for free shipping.</summary>
    [Range(0, 9_999_999)]
    public decimal Value { get; init; }

    [Range(0, 9_999_999)]
    public decimal? MaxDiscountAmount { get; init; }

    [Range(0, 99_999_999)]
    public decimal? MinSubtotal { get; init; }

    [Range(1, 9999)]
    public int? MinQuantity { get; init; }

    public bool FirstOrderOnly { get; init; }

    public IReadOnlyList<DeliveryZone> DeliveryZones { get; init; } = [];

    public IReadOnlyList<string> PaymentMethods { get; init; } = [];

    public DateTimeOffset? StartsAt { get; init; }

    public DateTimeOffset? EndsAt { get; init; }

    [Range(1, 1_000_000)]
    public int? UsageLimitTotal { get; init; }

    [Range(1, 1000)]
    public int? UsageLimitPerCustomer { get; init; }

    public bool Stackable { get; init; }

    [Range(-1000, 1000)]
    public int Priority { get; init; }

    public DiscountStatus Status { get; init; } = DiscountStatus.Draft;

    public IReadOnlyList<DiscountTargetDto> Targets { get; init; } = [];
}

/// <summary>The admin list's filters.</summary>
public class DiscountQueryDto
{
    /// <summary>Matches the name or the code, case-insensitively.</summary>
    [StringLength(100)]
    public string? Term { get; init; }

    public DiscountStatus? Status { get; init; }

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}

/// <summary>One redemption, for the usage report.</summary>
public class PromotionUsageDto
{
    public long OrderId { get; init; }

    public string OrderNumber { get; init; } = string.Empty;

    /// <summary>The code typed, or null for an automatic promotion.</summary>
    public string? Code { get; init; }

    /// <summary>Who. The account when there was one, otherwise the guest's number.</summary>
    public string ContactPhone { get; init; } = string.Empty;

    public bool IsMember { get; init; }

    public decimal Amount { get; init; }

    public DateTimeOffset UsedAt { get; init; }
}
