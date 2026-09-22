namespace WoodHeart.Domain.Enums.Promotions;

/// <summary>
/// What a discount does to the bill.
/// </summary>
/// <remarks>
/// <para>
/// Stored as text, so adding a member later is a code change rather than a
/// migration. PLAN.md §6.5 also lists <c>BuyXGetY</c> and <c>BundlePrice</c>;
/// they are deliberately absent until the engine implements them. An enum
/// member the engine does not understand is a discount an admin can create,
/// save, publish — and then watch do nothing, with no error anywhere.
/// </para>
/// </remarks>
public enum DiscountType
{
    /// <summary>A percentage off the goods it applies to. <c>Value</c> is 0–100.</summary>
    Percentage,

    /// <summary>A flat amount off the goods it applies to. <c>Value</c> is taka.</summary>
    FixedAmount,

    /// <summary>Takes the delivery charge off. <c>Value</c> is ignored.</summary>
    FreeShipping
}

/// <summary>
/// Where a discount is in its life.
/// </summary>
/// <remarks>
/// Separate from the start and end dates on purpose. A campaign that has not
/// been switched on yet and one whose window has not opened yet look the same
/// to a customer and are completely different to the person running the shop:
/// the first is unfinished work, the second is scheduled work.
/// </remarks>
public enum DiscountStatus
{
    /// <summary>Being written. Never applies, whatever its dates say.</summary>
    Draft,

    /// <summary>Live, subject to its window and its limits.</summary>
    Active,

    /// <summary>Switched off by hand, without losing the configuration.</summary>
    Paused,

    /// <summary>Finished with. Kept because orders point at it.</summary>
    Archived
}
