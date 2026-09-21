using System.ComponentModel.DataAnnotations;
using WoodHeart.Domain.Enums.Inventory;

namespace WoodHeart.Service.DTOs.Inventory;

/// <summary>One row of the admin stock list: a variant and where its count stands.</summary>
public class StockLevelDto
{
    public long VariantId { get; init; }

    public long ProductId { get; init; }

    public string ProductName { get; init; } = string.Empty;

    public string ProductSlug { get; init; } = string.Empty;

    public string Sku { get; init; } = string.Empty;

    public string VariantName { get; init; } = string.Empty;

    public string? ImagePath { get; init; }

    /// <summary>False until the first stock-in. An unstocked variant cannot be sold.</summary>
    public bool IsStocked { get; init; }

    public int OnHand { get; init; }

    public int Reserved { get; init; }

    public int Available { get; init; }

    public int? ReorderLevel { get; init; }

    /// <summary>At or below the reorder level, or the store threshold where none is set.</summary>
    public bool IsLow { get; init; }

    public DateTimeOffset? LastMovementAt { get; init; }
}

public class StockMovementDto
{
    public long Id { get; init; }

    public StockMovementType Type { get; init; }

    /// <summary>Signed: positive in, negative out.</summary>
    public int Quantity { get; init; }

    public int OnHandAfter { get; init; }

    public long? OrderId { get; init; }

    public string? OrderNumber { get; init; }

    public string? Reference { get; init; }

    public string? Reason { get; init; }

    public string PerformedBy { get; init; } = string.Empty;

    public DateTimeOffset OccurredAt { get; init; }
}

/// <summary>
/// A stock-in, a write-off, a correction or a transfer, recorded by hand.
/// </summary>
/// <remarks>
/// Never a <see cref="StockMovementType.Sale"/> — sales are recorded by
/// shipping the order, so the ledger and the order book cannot disagree.
/// </remarks>
public class AdjustStockDto
{
    [Required]
    public StockMovementType Type { get; init; }

    /// <summary>
    /// How many. Positive for everything but an Adjustment, which is signed:
    /// <c>-2</c> means "two fewer than the system said".
    /// </summary>
    [Required]
    public int Quantity { get; init; }

    /// <summary>Why. Required for an adjustment or damage; welcome on the rest.</summary>
    [MaxLength(500)]
    public string? Reason { get; init; }

    /// <summary>The supplier's invoice, the delivery note — whatever the paper says.</summary>
    [MaxLength(120)]
    public string? Reference { get; init; }

    /// <summary>Sets the variant's own reorder level at the same time, if given.</summary>
    [Range(0, 100_000)]
    public int? ReorderLevel { get; init; }
}

public class StockQueryDto
{
    [MaxLength(100)]
    public string? Term { get; init; }

    public bool LowOnly { get; init; }

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 50;
}

/// <summary>What the stock list needs to say at the top: how many lines need attention.</summary>
public class StockSummaryDto
{
    public int StockedVariants { get; init; }

    public int LowVariants { get; init; }

    public int UnstockedVariants { get; init; }
}
