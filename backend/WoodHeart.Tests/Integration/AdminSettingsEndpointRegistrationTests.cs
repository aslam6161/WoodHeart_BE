using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using WoodHeart.Domain.Constants;
using WoodHeart.Service.Interfaces.Common;

namespace WoodHeart.Tests.Integration;

/// <summary>
/// That the settings routes exist, and that both are Admin-only.
/// </summary>
/// <remarks>
/// The policy is the whole point of the check. This screen sets the VAT rate
/// the next order is charged; <c>RequireStaff</c> or <c>RequireAdminOrManager</c>
/// creeping onto it would let anyone with a till login change what the shop
/// charges in tax.
/// </remarks>
public class AdminSettingsEndpointRegistrationTests(WoodHeartApiFactory factory)
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
    [InlineData("GET", "api/admin/settings")]
    [InlineData("PUT", "api/admin/settings")]
    public void The_routes_are_registered_and_admin_only(string method, string pattern)
    {
        var endpoint = Find(method, pattern);

        endpoint.ShouldNotBeNull($"{method} /{pattern} is not routed");

        endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldBeNull();

        endpoint.Metadata.OfType<AuthorizeAttribute>()
            .ShouldContain(a => a.Policy == Policies.RequireAdmin, $"{method} /{pattern} must be Admin-only");
    }

    [Fact]
    public void The_service_is_registered()
    {
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetService<ISettingsAdminService>().ShouldNotBeNull();
    }
}
