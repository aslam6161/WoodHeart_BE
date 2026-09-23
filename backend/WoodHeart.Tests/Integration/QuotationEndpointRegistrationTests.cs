using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using WoodHeart.Domain.Constants;
using WoodHeart.Service.Interfaces.Quotations;

namespace WoodHeart.Tests.Integration;

/// <summary>
/// That the quotation routes exist, that a guest can answer one, and that
/// writing one is narrower than reading it.
/// </summary>
public class QuotationEndpointRegistrationTests(WoodHeartApiFactory factory)
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
    [InlineData("GET", "api/quotations/{quotationNumber}")]
    [InlineData("POST", "api/quotations/{quotationNumber}/accept")]
    [InlineData("POST", "api/quotations/{quotationNumber}/decline")]
    public void A_guest_can_read_and_answer_one(string method, string pattern)
    {
        // Most people quoted for a flat have no account, and asking them to
        // make one before they can say yes to a price is how a sale is lost.
        var endpoint = Find(method, pattern);

        endpoint.ShouldNotBeNull($"{method} /{pattern} is not routed");
        endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldNotBeNull();
    }

    [Fact]
    public void A_customer_sees_their_own_only_when_signed_in()
    {
        var endpoint = Find("GET", "api/quotations/my-quotations");

        endpoint.ShouldNotBeNull();
        endpoint.Metadata.OfType<AuthorizeAttribute>()
            .ShouldContain(a => a.Policy == Policies.RequireCustomer);
    }

    [Theory]
    [InlineData("GET", "api/admin/quotations")]
    [InlineData("GET", "api/admin/quotations/{quotationNumber}")]
    [InlineData("GET", "api/admin/quotations/{quotationNumber}/payment-methods")]
    [InlineData("POST", "api/admin/quotations")]
    [InlineData("PUT", "api/admin/quotations/{id:long}")]
    [InlineData("PUT", "api/admin/quotations/{quotationNumber}/status")]
    [InlineData("POST", "api/admin/quotations/{quotationNumber}/convert")]
    public void No_admin_route_is_open(string method, string pattern)
    {
        var endpoint = Find(method, pattern);

        endpoint.ShouldNotBeNull($"{method} /{pattern} is not routed");
        endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldBeNull();
        endpoint.Metadata.GetMetadata<IAuthorizeData>().ShouldNotBeNull();
    }

    [Theory]
    [InlineData("POST", "api/admin/quotations")]
    [InlineData("PUT", "api/admin/quotations/{id:long}")]
    [InlineData("PUT", "api/admin/quotations/{quotationNumber}/status")]
    [InlineData("POST", "api/admin/quotations/{quotationNumber}/convert")]
    public void Writing_one_is_narrower_than_reading_it(string method, string pattern)
    {
        // A quotation is a price the shop is held to, and it can be accepted
        // the moment it is sent. Whoever answers the telephone may read one;
        // writing it is a manager's.
        Find(method, pattern)!.Metadata
            .OfType<AuthorizeAttribute>()
            .ShouldContain(a => a.Policy == Policies.RequireAdminOrManager);
    }

    [Fact]
    public void The_service_is_registered()
    {
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetService<IQuotationService>().ShouldNotBeNull();
    }
}
