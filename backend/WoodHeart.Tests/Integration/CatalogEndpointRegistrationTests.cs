using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace WoodHeart.Tests.Integration;

/// <summary>
/// That every catalog route is registered, and that each one is anonymous or
/// authorized as intended.
/// </summary>
/// <remarks>
/// <para>
/// Reads the routing table out of the built application rather than sending
/// requests. The first version of these tests did send requests, and each
/// storefront call spent about eighteen seconds retrying a database connection
/// that was never going to succeed — the suite went from under a second to over
/// two minutes. A test suite that slow is one people stop running, which costs
/// more than the coverage was worth.
/// </para>
/// <para>
/// Inspecting the endpoints is also strictly more informative. A request tells
/// you the status code; the routing table tells you the route exists <b>and</b>
/// what authorization metadata it carries, without a database, a token, or a
/// second of waiting.
/// </para>
/// </remarks>
public partial class CatalogEndpointRegistrationTests(WoodHeartApiFactory factory)
    : IClassFixture<WoodHeartApiFactory>
{
    private IReadOnlyList<RouteEndpoint> Endpoints =>
        [.. factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()];

    private RouteEndpoint? Find(string method, string pattern) =>
        Endpoints.FirstOrDefault(e =>
            string.Equals(Normalize(e.RoutePattern.RawText), pattern, StringComparison.OrdinalIgnoreCase)
            && (e.Metadata.GetMetadata<HttpMethodMetadata>()
                    ?.HttpMethods.Contains(method, StringComparer.OrdinalIgnoreCase) ?? false));

    /// <summary>
    /// Strips route constraints, so <c>{id:long}</c> matches <c>{id}</c>.
    /// </summary>
    /// <remarks>
    /// These tests are about which routes exist and how they are protected.
    /// Baking <c>:long</c> into every expectation would make them fail the day
    /// a constraint is tightened, which is a change they should not have an
    /// opinion about.
    /// </remarks>
    private static string Normalize(string? rawPattern) =>
        rawPattern is null
            ? string.Empty
            : ConstraintPattern().Replace(rawPattern, "{$1}");

    [GeneratedRegex(@"\{([A-Za-z_][A-Za-z0-9_]*)(?::[^}]+)?\}")]
    private static partial Regex ConstraintPattern();

    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("GET", "api/catalog/categories")]
    [InlineData("GET", "api/catalog/products")]
    [InlineData("GET", "api/catalog/products/{slug}")]
    [InlineData("GET", "api/catalog/products/{slug}/related")]
    [InlineData("GET", "api/catalog/collections/{slug}")]
    [InlineData("GET", "api/catalog/collections/{slug}/products")]
    public void Storefront_routes_are_registered(string method, string pattern) =>
        Find(method, pattern).ShouldNotBeNull($"{method} /{pattern} is not routed");

    [Theory]
    [InlineData("GET", "api/catalog/categories")]
    [InlineData("GET", "api/catalog/products")]
    [InlineData("GET", "api/catalog/products/{slug}")]
    [InlineData("GET", "api/catalog/products/{slug}/related")]
    [InlineData("GET", "api/catalog/collections/{slug}")]
    [InlineData("GET", "api/catalog/collections/{slug}/products")]
    public void Storefront_routes_are_anonymous(string method, string pattern)
    {
        var endpoint = Find(method, pattern);

        endpoint.ShouldNotBeNull();

        // Explicit [AllowAnonymous], not merely the absence of [Authorize].
        // If a global fallback authorization policy is ever added — and one
        // should be, so a controller cannot be left open by forgetting an
        // attribute — the absence of this is what would silently lock the
        // storefront out of its own catalog.
        endpoint!.Metadata.GetMetadata<IAllowAnonymous>()
            .ShouldNotBeNull($"{method} /{pattern} is not marked [AllowAnonymous]");
    }

    [Theory]
    [InlineData("GET", "api/admin/categories")]
    [InlineData("POST", "api/admin/categories")]
    [InlineData("PUT", "api/admin/categories/{id}")]
    [InlineData("POST", "api/admin/categories/{id}/move")]
    [InlineData("DELETE", "api/admin/categories/{id}")]
    [InlineData("GET", "api/admin/brands")]
    [InlineData("POST", "api/admin/brands")]
    [InlineData("GET", "api/admin/products")]
    [InlineData("POST", "api/admin/products")]
    [InlineData("PUT", "api/admin/products/{id}")]
    [InlineData("POST", "api/admin/products/{id}/status")]
    [InlineData("DELETE", "api/admin/products/{id}")]
    [InlineData("POST", "api/admin/products/{productId}/variants")]
    [InlineData("PUT", "api/admin/products/variants/{variantId}")]
    [InlineData("DELETE", "api/admin/products/variants/{variantId}")]
    public void Admin_routes_are_registered_and_authorized(string method, string pattern)
    {
        var endpoint = Find(method, pattern);

        endpoint.ShouldNotBeNull($"{method} /{pattern} is not routed");

        endpoint!.Metadata.GetMetadata<IAuthorizeData>()
            .ShouldNotBeNull($"{method} /{pattern} carries no authorization requirement");

        // The inverse of the storefront check. An admin route that picked up
        // [AllowAnonymous] would publish the whole catalog editor.
        endpoint.Metadata.GetMetadata<IAllowAnonymous>()
            .ShouldBeNull($"{method} /{pattern} is anonymous and must not be");
    }

    [Fact]
    public void No_admin_route_is_reachable_without_authorization()
    {
        // A sweep rather than a list, so a route added later is covered without
        // anyone remembering to add it here.
        var unprotected = Endpoints
            .Where(e => e.RoutePattern.RawText?.StartsWith("api/admin", StringComparison.OrdinalIgnoreCase) == true)
            .Where(e => e.Metadata.GetMetadata<IAuthorizeData>() is null
                        || e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(e => e.RoutePattern.RawText)
            .ToList();

        unprotected.ShouldBeEmpty(
            $"these admin routes are not protected: {string.Join(", ", unprotected)}");
    }
}
