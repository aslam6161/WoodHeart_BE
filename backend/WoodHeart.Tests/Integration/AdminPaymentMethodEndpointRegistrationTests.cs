using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using WoodHeart.Domain.Constants;
using WoodHeart.Service.Interfaces.Payments;

namespace WoodHeart.Tests.Integration;

/// <summary>
/// That the payment-method routes exist, that all of them are Admin-only, and
/// that no route can conjure a method or remove one.
/// </summary>
/// <remarks>
/// The policy is the whole point of the first check. These routes decide
/// whether money can be taken, how much extra a customer is charged for the
/// way they pay, and which merchant account a gateway points at — and they
/// carry credentials. <c>RequireStaff</c> creeping onto them would let anyone
/// with a till login switch the shop to somebody else's bKash account.
/// </remarks>
public class AdminPaymentMethodEndpointRegistrationTests(WoodHeartApiFactory factory)
    : IClassFixture<WoodHeartApiFactory>
{
    private IEnumerable<RouteEndpoint> Endpoints =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();

    private RouteEndpoint? Find(string method, string pattern) =>
        Endpoints.FirstOrDefault(e =>
            string.Equals(e.RoutePattern.RawText, pattern, StringComparison.OrdinalIgnoreCase)
            && (e.Metadata.GetMetadata<HttpMethodMetadata>()
                    ?.HttpMethods.Contains(method, StringComparer.OrdinalIgnoreCase) ?? false));

    [Theory]
    [InlineData("GET", "api/admin/payment-methods")]
    [InlineData("GET", "api/admin/payment-methods/{code}")]
    [InlineData("PUT", "api/admin/payment-methods/{code}")]
    public void The_routes_are_registered_and_admin_only(string method, string pattern)
    {
        var endpoint = Find(method, pattern);

        endpoint.ShouldNotBeNull($"{method} /{pattern} is not routed");

        endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldBeNull();

        endpoint.Metadata.OfType<AuthorizeAttribute>()
            .ShouldContain(
                a => a.Policy == Policies.RequireAdmin,
                $"{method} /{pattern} must be Admin-only");
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public void Nothing_here_creates_or_removes_a_method(string method)
    {
        // A method's code is the join to the class that implements it. A row
        // invented from a screen would read as configured and never appear at
        // checkout, with nothing anywhere to say why — so the route to make one
        // deliberately does not exist.
        var found = Endpoints
            .Where(e => e.RoutePattern.RawText?.StartsWith(
                "api/admin/payment-methods", StringComparison.OrdinalIgnoreCase) == true)
            .Where(e => e.Metadata.GetMetadata<HttpMethodMetadata>()
                ?.HttpMethods.Contains(method, StringComparer.OrdinalIgnoreCase) == true)
            .ToList();

        found.ShouldBeEmpty($"{method} on a payment method must not be routed");
    }

    [Fact]
    public void The_service_is_registered()
    {
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetService<IPaymentMethodAdminService>().ShouldNotBeNull();
    }
}
