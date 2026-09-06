using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using WoodHeart.Domain.Constants;
using WoodHeart.Service.Interfaces.Ordering;
using WoodHeart.Service.Interfaces.Payments;

namespace WoodHeart.Tests.Integration;

/// <summary>
/// That checkout and the order routes exist, are open to the right callers, and
/// that everything behind them resolves from the real container.
/// </summary>
/// <remarks>
/// <para>
/// Reads the routing table rather than sending requests, for the reason
/// <see cref="CatalogEndpointRegistrationTests"/> gives at length.
/// </para>
/// <para>
/// The two checks that earn their place: <b>checkout must be anonymous</b>,
/// because guest checkout is the main path and an <c>[Authorize]</c> that crept
/// on would turn every signed-out order into a 401 — and <b>the order routes
/// must not be</b>, because they return somebody's delivery address.
/// </para>
/// </remarks>
public class CheckoutEndpointRegistrationTests(WoodHeartApiFactory factory)
    : IClassFixture<WoodHeartApiFactory>
{
    private IReadOnlyList<RouteEndpoint> Endpoints =>
        [.. factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()];

    private RouteEndpoint? Find(string method, string pattern) =>
        Endpoints.FirstOrDefault(e =>
            string.Equals(e.RoutePattern.RawText, pattern, StringComparison.OrdinalIgnoreCase)
            && (e.Metadata.GetMetadata<HttpMethodMetadata>()
                    ?.HttpMethods.Contains(method, StringComparer.OrdinalIgnoreCase) ?? false));

    [Theory]
    [InlineData("POST", "api/checkout/payment-methods")]
    [InlineData("POST", "api/checkout/place-order")]
    [InlineData("GET", "api/orders")]
    [InlineData("GET", "api/orders/{orderNumber}")]
    [InlineData("POST", "api/orders/{orderNumber}/cancel")]
    [InlineData("POST", "api/orders/lookup")]
    public void The_routes_are_registered(string method, string pattern) =>
        Find(method, pattern).ShouldNotBeNull($"{method} /{pattern} is not routed");

    [Theory]
    [InlineData("POST", "api/checkout/payment-methods")]
    [InlineData("POST", "api/checkout/place-order")]
    public void Checkout_works_without_an_account(string method, string pattern)
    {
        // Guest checkout is the main path in this market. Most of the shop's
        // customers will never create an account, and an [Authorize] here would
        // stop all of them buying anything.
        Find(method, pattern)!.Metadata.GetMetadata<IAllowAnonymous>()
            .ShouldNotBeNull($"{method} /{pattern} is not marked [AllowAnonymous]");
    }

    [Theory]
    [InlineData("GET", "api/orders")]
    [InlineData("GET", "api/orders/{orderNumber}")]
    [InlineData("POST", "api/orders/{orderNumber}/cancel")]
    public void A_customers_own_orders_need_an_account(string method, string pattern)
    {
        // These return a name, a phone number and a delivery address. An
        // [AllowAnonymous] that crept onto this controller would hand them to
        // anyone who could guess an order number.
        var endpoint = Find(method, pattern);

        endpoint!.Metadata.GetMetadata<IAllowAnonymous>()
            .ShouldBeNull($"{method} /{pattern} must not be anonymous");

        endpoint.Metadata.GetMetadata<IAuthorizeData>()
            .ShouldNotBeNull($"{method} /{pattern} is not marked [Authorize]");
    }

    [Fact]
    public void The_guest_lookup_is_open_but_rate_limited_tightly()
    {
        var endpoint = Find("POST", "api/orders/lookup");

        // Open by necessity — a guest still needs to see where their sofa is.
        endpoint!.Metadata.GetMetadata<IAllowAnonymous>().ShouldNotBeNull();

        // And on the sensitive policy, because it is the one route here that
        // takes an order number and could otherwise be walked.
        endpoint.Metadata
            .OfType<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()
            .ShouldContain(a => a.PolicyName == RateLimitPolicies.Sensitive);
    }

    [Fact]
    public void The_checkout_services_resolve_from_the_container()
    {
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetService<ICheckoutService>()
            .ShouldNotBeNull("ICheckoutService is not registered");

        scope.ServiceProvider.GetService<IOrderService>()
            .ShouldNotBeNull("IOrderService is not registered");

        scope.ServiceProvider.GetService<IPaymentProviderResolver>()
            .ShouldNotBeNull("IPaymentProviderResolver is not registered");
    }

    [Fact]
    public void Cash_on_delivery_is_registered_as_a_payment_provider()
    {
        using var scope = factory.Services.CreateScope();

        var providers = scope.ServiceProvider.GetServices<IPaymentProvider>().ToList();

        // Registered as IPaymentProvider rather than as itself, because that is
        // what the resolver enumerates. A provider bound to its own type would
        // be configured, look enabled in the admin screen, and never appear at
        // checkout.
        providers.ShouldContain(p => p.Code == PaymentMethodCodes.CashOnDelivery);
    }
}
