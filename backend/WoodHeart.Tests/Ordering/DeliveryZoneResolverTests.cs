using WoodHeart.Domain.Enums.Ordering;
using WoodHeart.Domain.Ordering;
using WoodHeart.Domain.ValueObjects;

namespace WoodHeart.Tests.Ordering;

/// <summary>
/// Which zone an address falls in — and therefore what delivery costs.
/// </summary>
/// <remarks>
/// The rule that carries the money: <b>Dhaka district is not "inside
/// Dhaka"</b>. Savar and Dhamrai are an hour out and are a different job for a
/// van than Dhanmondi, and a resolver that read only the district would ship
/// them at the city rate.
/// </remarks>
public class DeliveryZoneResolverTests
{
    private static DeliveryAddress At(string district, string? upazila = null, string? area = null) =>
        DeliveryAddress.Create("Dhaka", district, "House 12, Road 3", upazila, area);

    [Fact]
    public void A_Dhaka_city_address_is_inside_Dhaka() =>
        DeliveryZoneResolver.Resolve(At("Dhaka", area: "Dhanmondi"))
            .ShouldBe(DeliveryZone.InsideDhaka);

    [Fact]
    public void Anywhere_else_in_the_country_is_outside_Dhaka() =>
        DeliveryZoneResolver.Resolve(At("Sylhet")).ShouldBe(DeliveryZone.OutsideDhaka);

    [Theory]
    [InlineData("Savar")]
    [InlineData("Dhamrai")]
    [InlineData("Keraniganj")]
    [InlineData("Dohar")]
    [InlineData("Nawabganj")]
    public void The_outlying_parts_of_Dhaka_district_are_not_inside_Dhaka(string upazila)
    {
        // The district reaches an hour out of the city. Pricing these at the
        // city rate is a loss on exactly the deliveries that cost the most to
        // make.
        DeliveryZoneResolver.Resolve(At("Dhaka", upazila))
            .ShouldBe(DeliveryZone.OutsideDhaka);
    }

    [Fact]
    public void The_district_is_matched_regardless_of_how_it_was_typed()
    {
        // A dropdown will be consistent; a typed address will not, and an
        // address entered as "dhaka" must not be charged the out-of-town rate.
        DeliveryZoneResolver.Resolve(At("dhaka")).ShouldBe(DeliveryZone.InsideDhaka);
        DeliveryZoneResolver.Resolve(At("DHAKA")).ShouldBe(DeliveryZone.InsideDhaka);
        DeliveryZoneResolver.Resolve(At(" Dhaka ")).ShouldBe(DeliveryZone.InsideDhaka);
    }

    [Fact]
    public void An_upazila_inside_the_city_stays_inside_Dhaka() =>
        DeliveryZoneResolver.Resolve(At("Dhaka", "Mirpur")).ShouldBe(DeliveryZone.InsideDhaka);
}
