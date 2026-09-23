using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using WoodHeart.Domain.Constants;
using WoodHeart.Service.Interfaces.Consultations;

namespace WoodHeart.Tests.Integration;

/// <summary>
/// That the consultation routes exist, that a guest can reach the ones they
/// have to, and that the ones that write the schedule are narrower.
/// </summary>
public class ConsultationEndpointRegistrationTests(WoodHeartApiFactory factory)
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
    [InlineData("GET", "api/consultations/services")]
    [InlineData("GET", "api/consultations/services/{slug}")]
    [InlineData("GET", "api/consultations/consultants")]
    [InlineData("GET", "api/consultations/availability")]
    [InlineData("POST", "api/consultations/bookings")]
    [InlineData("GET", "api/consultations/bookings/{bookingNumber}")]
    [InlineData("POST", "api/consultations/bookings/{bookingNumber}/cancel")]
    public void A_guest_can_browse_and_book(string method, string pattern)
    {
        // Booking without an account is the main path, not an edge case:
        // somebody who wants a consultant to look at their flat should not
        // have to make one first.
        var endpoint = Find(method, pattern);

        endpoint.ShouldNotBeNull($"{method} /{pattern} is not routed");
        endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldNotBeNull();
    }

    [Fact]
    public void A_customer_sees_their_own_bookings_only_when_signed_in()
    {
        var endpoint = Find("GET", "api/consultations/my-bookings");

        endpoint.ShouldNotBeNull();
        endpoint.Metadata.OfType<AuthorizeAttribute>()
            .ShouldContain(a => a.Policy == Policies.RequireCustomer);
    }

    [Theory]
    [InlineData("GET", "api/admin/consultations/services")]
    [InlineData("POST", "api/admin/consultations/services")]
    [InlineData("PUT", "api/admin/consultations/services/{id:long}")]
    [InlineData("GET", "api/admin/consultations/consultants")]
    [InlineData("GET", "api/admin/consultations/consultants/{id:long}")]
    [InlineData("POST", "api/admin/consultations/consultants")]
    [InlineData("PUT", "api/admin/consultations/consultants/{id:long}")]
    [InlineData("PUT", "api/admin/consultations/consultants/{id:long}/schedule")]
    [InlineData("GET", "api/admin/consultations/bookings/{bookingNumber}")]
    [InlineData("PUT", "api/admin/consultations/bookings/{bookingNumber}/status")]
    public void No_admin_route_is_open(string method, string pattern)
    {
        var endpoint = Find(method, pattern);

        endpoint.ShouldNotBeNull($"{method} /{pattern} is not routed");
        endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldBeNull();
        endpoint.Metadata.GetMetadata<IAuthorizeData>().ShouldNotBeNull();
    }

    [Theory]
    [InlineData("POST", "api/admin/consultations/services")]
    [InlineData("PUT", "api/admin/consultations/services/{id:long}")]
    [InlineData("POST", "api/admin/consultations/consultants")]
    [InlineData("PUT", "api/admin/consultations/consultants/{id:long}/schedule")]
    public void Writing_the_schedule_is_narrower_than_reading_it(string method, string pattern)
    {
        // Whoever answers the telephone needs to see when the next site visit
        // is free. A rule typed wrong is a day nobody can book, or a day
        // somebody is expected to work and does not know it.
        Find(method, pattern)!.Metadata
            .OfType<AuthorizeAttribute>()
            .ShouldContain(a => a.Policy == Policies.RequireAdminOrManager);
    }

    [Fact]
    public void The_services_are_registered()
    {
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetService<IAvailabilityService>().ShouldNotBeNull();
        scope.ServiceProvider.GetService<IBookingService>().ShouldNotBeNull();
        scope.ServiceProvider.GetService<IConsultationAdminService>().ShouldNotBeNull();
    }
}
