namespace WoodHeart.Domain.Entity.Common;

/// <summary>
/// A counter behind a human-facing number, one row per sequence per period.
/// </summary>
/// <remarks>
/// <para>
/// <b>The obvious implementation is wrong.</b> <c>SELECT MAX(order_number) + 1</c>
/// is what everyone writes first, and it hands two customers checking out in
/// the same second the same order number. The unique index then rejects one of
/// them and a real sale fails at the last step. This row exists so the
/// allocation is a single atomic <c>UPDATE ... RETURNING</c> instead.
/// </para>
/// <para>
/// <b>Gaps are expected and fine.</b> A number is taken before the order is
/// committed, so a failed placement burns one. That is the correct trade for an
/// order number: uniqueness and no contention matter, an unbroken run does not.
/// (An invoice series, where a regulator may require no gaps, would need
/// different handling — worth remembering when the invoice work lands.)
/// </para>
/// </remarks>
public class NumberSequence : BaseEntity
{
    /// <summary>What is being numbered: <c>order</c>, <c>booking</c>, <c>quotation</c>.</summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// The bucket the counter resets in — <c>2609</c> for September 2026.
    /// </summary>
    /// <remarks>
    /// Monthly, so an order number carries its own date: someone reading
    /// <c>WH-2609-00042</c> off a delivery slip knows when it was placed
    /// without looking it up. A single ever-growing counter would also
    /// advertise the shop's monthly volume to anyone who placed two orders.
    /// </remarks>
    public string Period { get; set; } = null!;

    /// <summary>The last value handed out. The next allocation returns this plus one.</summary>
    public int NextValue { get; set; }
}
