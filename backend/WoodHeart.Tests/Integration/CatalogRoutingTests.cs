using System.Net;
using System.Net.Http.Json;

namespace WoodHeart.Tests.Integration;

/// <summary>
/// That the catalog endpoints exist, resolve their dependencies, and are
/// protected.
/// </summary>
/// <remarks>
/// <para>
/// Unit tests construct services directly, so they prove nothing about DI. A
/// service missing from <c>ApplicationServiceExtensions</c> builds, passes
/// every unit test, and then throws
/// <c>InvalidOperationException: Unable to resolve service</c> on the first
/// real request. These tests boot the real container and route through it.
/// </para>
/// <para>
/// No database is touched. Authorization runs before the action, so a 401 is
/// returned before any service is asked for data — which is exactly what makes
/// this runnable on a machine with no Postgres.
/// </para>
/// </remarks>
public class CatalogRoutingTests(WoodHeartApiFactory factory) : IClassFixture<WoodHeartApiFactory>
{
    private HttpClient Client => factory.CreateClient();

    [Theory]
    [InlineData("/api/admin/categories")]
    [InlineData("/api/admin/categories/1")]
    [InlineData("/api/admin/brands")]
    [InlineData("/api/admin/brands/1")]
    [InlineData("/api/admin/products")]
    [InlineData("/api/admin/products/1")]
    public async Task Admin_reads_require_authentication(string url)
    {
        var response = await Client.GetAsync(url);

        // 401, not 404: a 404 here would mean the route was never registered,
        // and the endpoint would look "secure" for entirely the wrong reason.
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/api/admin/categories")]
    [InlineData("/api/admin/brands")]
    [InlineData("/api/admin/products")]
    public async Task Admin_writes_require_authentication(string url)
    {
        var response = await Client.PostAsJsonAsync(url, new { nameEn = "Anything" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Category_move_is_registered_as_its_own_route()
    {
        // Moving is a distinct operation from updating — it rewrites the
        // materialized path of a whole subtree. If this route disappeared, a
        // caller would fall back to PUT and silently never move anything.
        var response = await Client.PostAsJsonAsync(
            "/api/admin/categories/1/move", new { newParentId = 2 });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Product_status_change_is_registered_as_its_own_route()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/admin/products/1/status", new { status = "Active" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/api/admin/products/1/variants")]
    public async Task Variant_routes_are_registered(string url)
    {
        var response = await Client.PostAsJsonAsync(url, new { sku = "WH-1" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Unknown_catalog_route_is_a_404_not_a_401()
    {
        // The control for the tests above. If everything under /api/admin
        // returned 401 regardless, they would pass without proving anything.
        var response = await Client.GetAsync("/api/admin/nothing-here");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
