using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using WoodHeart.Domain.Constants;
using WoodHeart.Service.Interfaces.Promotions;

namespace WoodHeart.Tests.Integration;

/// <summary>
/// That the discount routes exist, none is open, and the coupon endpoints on
/// the basket are.
/// </summary>
public class AdminDiscountEndpointRegistrationTests(WoodHeartApiFactory factory)
    : IClassFixture<WoodHeartApiFactory>
{
    private RouteEndpoint? Find(string method, string pattern) =>
        factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .FirstOrDefault(e =>
                string.Equals(e.RoutePattern.RawText, pattern, StringComparison.OrdinalIgnoreCase)
                && (e.Metadata.GetMetadata<HttpMethodMetadata>()
                        ?.HttpMethods.Contains(method, StringComparer.OrdinalIgnoreCase) ?? false));

    [Theory]
    [InlineData("GET", "api/admin/discounts")]
    [InlineData("GET", "api/admin/discounts/{id:long}")]
    [InlineData("POST", "api/admin/discounts")]
    [InlineData("PUT", "api/admin/discounts/{id:long}")]
    [InlineData("PUT", "api/admin/discounts/{id:long}/status")]
    [InlineData("GET", "api/admin/discounts/{id:long}/usage")]
    public void The_admin_routes_are_registered_and_none_is_open(string method, string pattern)
    {
        var endpoint = Find(method, pattern);

        endpoint.ShouldNotBeNull($"{method} /{pattern} is not routed");
        endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldBeNull();
        endpoint.Metadata.GetMetadata<IAuthorizeData>().ShouldNotBeNull();
    }

    [Theory]
    [InlineData("POST", "api/admin/discounts")]
    [InlineData("PUT", "api/admin/discounts/{id:long}")]
    [InlineData("PUT", "api/admin/discounts/{id:long}/status")]
    public void Writing_a_discount_is_narrower_than_reading_one(string method, string pattern)
    {
        // Every row here is money the shop has decided to give away, and a
        // misplaced decimal point on a percentage is an expensive typo.
        Find(method, pattern)!.Metadata
            .OfType<AuthorizeAttribute>()
            .ShouldContain(a => a.Policy == Policies.RequireAdminOrManager);
    }

    [Theory]
    [InlineData("POST", "api/cart/coupons")]
    [InlineData("DELETE", "api/cart/coupons/{code}")]
    public void The_basket_coupon_routes_are_open_to_guests(string method, string pattern)
    {
        // Guest checkout is the main path here. A coupon endpoint behind a
        // login would be a discount only members could use, which is not what
        // anybody meant by "coupon".
        var endpoint = Find(method, pattern);

        endpoint.ShouldNotBeNull($"{method} /{pattern} is not routed");
        endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldNotBeNull();
    }

    [Fact]
    public void The_services_are_registered()
    {
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetService<IPromotionService>().ShouldNotBeNull();
        scope.ServiceProvider.GetService<IDiscountAdminService>().ShouldNotBeNull();
    }
}
