using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Domain.Ordering;

/// <summary>
/// Decides which delivery zone an address falls in.
/// </summary>
/// <remarks>
/// <para>
/// <b>The zone is decided by the address, not by what the customer picked.</b>
/// The cart lets them choose one so the page can quote something before they
/// have typed an address; at placement that choice is replaced by what the
/// address actually says. Charging a Dhaka rate to deliver to Sylhet because a
/// dropdown was left alone is the shop's loss, and it is a loss on exactly the
/// orders that travel furthest.
/// </para>
/// <para>
/// <b>Dhaka city, not Dhaka district.</b> The district reaches Savar,
/// Dhamrai and Nawabganj — an hour or more out, and a different job for a van
/// than Dhanmondi. Those are listed as outside, which is what the shop's own
/// rate card means by "inside Dhaka".
/// </para>
/// <para>
/// A lookup rather than a geocoder because it has to be right about the
/// twenty names that matter and cheap enough to run on every checkout keystroke.
/// When the shop's coverage grows past two zones this becomes a table; today it
/// would be a table with two rows.
/// </para>
/// </remarks>
public static class DeliveryZoneResolver
{
    /// <summary>
    /// Parts of Dhaka district that the trade does not treat as "inside Dhaka".
    /// </summary>
    /// <remarks>
    /// Checked before the district, so an address in Savar is priced as the
    /// out-of-town delivery it is even though its district says Dhaka.
    /// </remarks>
    private static readonly HashSet<string> OutlyingUpazilas =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Savar", "Dhamrai", "Nawabganj", "Dohar", "Keraniganj",
            "সাভার", "ধামরাই", "নবাবগঞ্জ", "দোহার", "কেরানীগঞ্জ"
        };

    /// <summary>
    /// The district, in both scripts.
    /// </summary>
    /// <remarks>
    /// Half of this shop's customers will type the address in Bangla, and
    /// "ঢাকা" compared to "Dhaka" is a miss — which priced every Bangla
    /// address in the city as an out-of-town delivery. The storefront's
    /// dropdowns send English today; the API must not depend on that, because
    /// the phone order that staff type in will not.
    /// </remarks>
    private static readonly HashSet<string> DhakaDistrict =
        new(StringComparer.OrdinalIgnoreCase) { "Dhaka", "ঢাকা" };

    public static DeliveryZone Resolve(DeliveryAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.District is not { } district || !DhakaDistrict.Contains(district.Trim()))
        {
            return DeliveryZone.OutsideDhaka;
        }

        return address.Upazila is { } upazila && OutlyingUpazilas.Contains(upazila.Trim())
            ? DeliveryZone.OutsideDhaka
            : DeliveryZone.InsideDhaka;
    }
}
