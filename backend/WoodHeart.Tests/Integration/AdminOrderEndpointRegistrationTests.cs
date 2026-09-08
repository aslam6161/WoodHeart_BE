using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using WoodHeart.Domain.Constants;
using WoodHeart.Service.Interfaces.Ordering;

namespace WoodHeart.Tests.Integration;

/// <summary>
/// That the admin order routes exist, and that none of them is open.
/// </summary>
/// <remarks>
/// <para>
/// Reads the routing table rather than sending requests, for the reason
/// <see cref="CatalogEndpointRegistrationTests"/> gives at length.
/// </para>
/// <para>
/// The check that earns its place: <b>every route here needs a policy.</b>
/// These return unmasked phone numbers and full delivery addresses for every
/// order in the shop, and an <c>[AllowAnonymous]</c> that crept onto the
/// controller would publish the customer list. The two that move money are
/// checked separately, because they carry a narrower policy than the rest.
/// </para>
/// </remarks>
public class AdminOrderEndpointRegistrationTests(WoodHeartApiFactory factory)
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
    [InlineData("GET", "api/admin/orders")]
    [InlineData("GET", "api/admin/orders/status-counts")]
    [InlineData("GET", "api/admin/orders/{orderNumber}")]
    [InlineData("POST", "api/admin/orders/{orderNumber}/status")]
    [InlineData("POST", "api/admin/orders/{orderNumber}/fulfilment")]
    [InlineData("POST", "api/admin/orders/{orderNumber}/payment")]
    [InlineData("POST", "api/admin/orders/{orderNumber}/delivery-fee")]
    [InlineData("PUT", "api/admin/orders/{orderNumber}/notes")]
    [InlineData("GET", "api/admin/orders/{orderNumber}/invoice")]
    public void The_routes_are_registered(string method, string pattern) =>
        Find(method, pattern).ShouldNotBeNull($"{method} /{pattern} is not routed");

    [Theory]
    [InlineData("GET", "api/admin/orders")]
    [InlineData("GET", "api/admin/orders/status-counts")]
    [InlineData("GET", "api/admin/orders/{orderNumber}")]
    [InlineData("POST", "api/admin/orders/{orderNumber}/status")]
    [InlineData("POST", "api/admin/orders/{orderNumber}/fulfilment")]
    [InlineData("POST", "api/admin/orders/{orderNumber}/payment")]
    [InlineData("POST", "api/admin/orders/{orderNumber}/delivery-fee")]
    [InlineData("PUT", "api/admin/orders/{orderNumber}/notes")]
    [InlineData("GET", "api/admin/orders/{orderNumber}/invoice")]
    public void Nothing_on_the_order_board_is_open(string method, string pattern)
    {
        var endpoint = Find(method, pattern);

        endpoint!.Metadata.GetMetadata<IAllowAnonymous>()
            .ShouldBeNull($"{method} /{pattern} must not be anonymous");

        endpoint.Metadata.GetMetadata<IAuthorizeData>()
            .ShouldNotBeNull($"{method} /{pattern} is not behind a policy");
    }

    [Theory]
    [InlineData("POST", "api/admin/orders/{orderNumber}/payment")]
    [InlineData("POST", "api/admin/orders/{orderNumber}/delivery-fee")]
    public void The_two_that_move_money_are_narrower_than_the_rest(string method, string pattern)
    {
        // Packing a wardrobe and marking an order paid are different jobs.
        // Everything else on this controller is the daily work of the shop;
        // these two are claims about cash, and they belong to whoever is
        // accountable for the till.
        Find(method, pattern)!.Metadata
            .OfType<AuthorizeAttribute>()
            .ShouldContain(a => a.Policy == Policies.RequireAdminOrManager);
    }

    [Fact]
    public void The_rest_of_the_board_is_open_to_staff()
    {
        // An order board only a manager can touch is an order board nobody
        // uses, and the work goes back onto paper.
        Find("POST", "api/admin/orders/{orderNumber}/status")!.Metadata
            .OfType<AuthorizeAttribute>()
            .ShouldContain(a => a.Policy == Policies.RequireStaff);
    }

    [Fact]
    public void The_admin_order_service_resolves_from_the_container()
    {
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetService<IAdminOrderService>()
            .ShouldNotBeNull("IAdminOrderService is not registered");
    }

    [Fact]
    public void The_invoice_endpoint_does_not_restrict_what_it_may_return()
    {
        // A regression guard for a bug this endpoint actually had.
        // [Produces("application/pdf")] reads as documentation, but it sets the
        // content types the result may be formatted as — and the success path
        // is a FileContentResult that writes its own. It left no formatter for
        // the GeneralResponse on the failure path, so asking for an order
        // number that does not exist answered 406 Not Acceptable with an empty
        // body instead of the 404 the Angular client knows how to read.
        // [ProducesResponseType] documents the same thing and changes nothing.
        Find("GET", "api/admin/orders/{orderNumber}/invoice")!.Metadata
            .OfType<ProducesAttribute>()
            .ShouldBeEmpty("[Produces] here breaks the 404 into a 406");
    }

    [Fact]
    public void The_invoice_service_resolves_from_the_container()
    {
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetService<IInvoiceService>()
            .ShouldNotBeNull("IInvoiceService is not registered");
    }
}
