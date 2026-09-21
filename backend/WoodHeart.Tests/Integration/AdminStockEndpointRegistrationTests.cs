using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using WoodHeart.Domain.Constants;
using WoodHeart.Service.Interfaces.Inventory;

namespace WoodHeart.Tests.Integration;

/// <summary>That the stock routes exist, none is open, and writing is narrower than reading.</summary>
public class AdminStockEndpointRegistrationTests(WoodHeartApiFactory factory)
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
    [InlineData("GET", "api/admin/stock")]
    [InlineData("GET", "api/admin/stock/summary")]
    [InlineData("GET", "api/admin/stock/{variantId:long}")]
    [InlineData("GET", "api/admin/stock/{variantId:long}/movements")]
    [InlineData("POST", "api/admin/stock/{variantId:long}/movements")]
    public void The_routes_are_registered_and_none_is_open(string method, string pattern)
    {
        var endpoint = Find(method, pattern);

        endpoint.ShouldNotBeNull($"{method} /{pattern} is not routed");
        endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldBeNull();
        endpoint.Metadata.GetMetadata<IAuthorizeData>().ShouldNotBeNull();
    }

    [Fact]
    public void Writing_a_movement_is_narrower_than_reading_the_count()
    {
        // The packer needs to see the count; the stock-in that did not
        // happen and the write-off that did are both money.
        Find("POST", "api/admin/stock/{variantId:long}/movements")!.Metadata
            .OfType<AuthorizeAttribute>()
            .ShouldContain(a => a.Policy == Policies.RequireAdminOrManager);
    }

    [Fact]
    public void The_service_is_registered()
    {
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetService<IInventoryService>().ShouldNotBeNull();
    }
}
