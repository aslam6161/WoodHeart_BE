using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using WoodHeart.Service.Interfaces.Ordering;

namespace WoodHeart.Tests.Integration;

/// <summary>
/// That the cart routes exist, work without an account, and that everything
/// behind them resolves from the real container.
/// </summary>
/// <remarks>
/// <para>
/// Reads the routing table rather than sending requests, for the reason
/// <see cref="CatalogEndpointRegistrationTests"/> gives at length: a request
/// against a machine with no Postgres spends eighteen seconds failing to
/// connect, and a suite that slow stops being run.
/// </para>
/// <para>
/// The DI check at the bottom is the one that catches a real class of mistake.
/// <c>CartService</c> can be constructed directly by every unit test in
/// <c>CartServiceTests</c> and still be missing from
/// <c>ApplicationServiceExtensions</c> — in which case the first customer to
/// open their basket gets a 500.
/// </para>
/// </remarks>
public class CartEndpointRegistrationTests(WoodHeartApiFactory factory)
    : IClassFixture<WoodHeartApiFactory>
{
    private IReadOnlyList<RouteEndpoint> Endpoints =>
        [.. factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()];

    private RouteEndpoint? Find(string method, string pattern) =>
        Endpoints.FirstOrDefault(e =>
            string.Equals(
                e.RoutePattern.RawText?.Replace(":long", string.Empty, StringComparison.Ordinal),
                pattern,
                StringComparison.OrdinalIgnoreCase)
            && (e.Metadata.GetMetadata<HttpMethodMetadata>()
                    ?.HttpMethods.Contains(method, StringComparer.OrdinalIgnoreCase) ?? false));

    [Theory]
    [InlineData("GET", "api/cart")]
    [InlineData("POST", "api/cart/items")]
    [InlineData("PUT", "api/cart/items/{lineId}")]
    [InlineData("DELETE", "api/cart/items/{lineId}")]
    [InlineData("DELETE", "api/cart")]
    [InlineData("PUT", "api/cart/delivery-zone")]
    public void Cart_routes_are_registered(string method, string pattern) =>
        Find(method, pattern).ShouldNotBeNull($"{method} /{pattern} is not routed");

    [Theory]
    [InlineData("GET", "api/cart")]
    [InlineData("POST", "api/cart/items")]
    [InlineData("PUT", "api/cart/items/{lineId}")]
    [InlineData("DELETE", "api/cart/items/{lineId}")]
    [InlineData("DELETE", "api/cart")]
    [InlineData("PUT", "api/cart/delivery-zone")]
    public void Cart_routes_work_without_an_account(string method, string pattern)
    {
        var endpoint = Find(method, pattern);

        endpoint.ShouldNotBeNull();

        // Guest checkout is the main path here, not an edge case. An
        // [Authorize] that crept onto this controller would turn every
        // signed-out visitor's basket into a 401 — and most of the shop's
        // customers will never create an account.
        endpoint!.Metadata.GetMetadata<IAllowAnonymous>()
            .ShouldNotBeNull($"{method} /{pattern} is not marked [AllowAnonymous]");
    }

    [Fact]
    public void The_cart_services_resolve_from_the_container()
    {
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetService<ICartService>()
            .ShouldNotBeNull("ICartService is not registered");

        scope.ServiceProvider.GetService<IPricingContextFactory>()
            .ShouldNotBeNull("IPricingContextFactory is not registered");
    }
}
